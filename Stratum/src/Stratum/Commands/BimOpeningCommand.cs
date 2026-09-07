using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Modeling;

namespace Stratum.Commands
{
  /// <summary>
  /// Inserts a window, door or plain opening into a wall.
  ///
  /// The opening is hosted: it is stored as a station along the wall's baseline,
  /// so it travels with the wall. Every layer of the assembly is then cut on its
  /// own terms - the finish can wrap the reveal, the sheathing can butt the rough
  /// opening, the cladding can return to the frame - which is what makes the
  /// jamb, head and sill details in the drawings agree with the model.
  /// </summary>
  [Guid("feb36994-c610-4df4-a6c4-b8e7a77c708b")]
  public class BimOpeningCommand : Command
  {
    public BimOpeningCommand() { Instance = this; }
    public static BimOpeningCommand Instance { get; private set; }

    public override string EnglishName => "BimOpening";

    static readonly string[] KindNames = { "Window", "Door", "Opening" };

    // Remembered between runs, the way Rhino's own commands behave.
    static int _kind = 0;
    static double _width = 36.0;
    static double _height = 48.0;
    static double _sill = 36.0;
    static double _clearance = 0.5;
    static Guid _unitId = Guid.Empty;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);
      if (model.Walls.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: there are no walls in this document yet. Run BimWall first.");
        return Result.Nothing;
      }

      // ---- host wall -------------------------------------------------------
      var go = new GetObject();
      go.SetCommandPrompt("Select the wall to receive the opening");
      go.GeometryFilter = ObjectType.Brep;
      go.SubObjectSelect = false;
      go.EnablePreSelect(true, true);
      go.SetCustomGeometryFilter((rhObject, geometry, componentIndex) =>
        rhObject != null && !string.IsNullOrEmpty(rhObject.Attributes.GetUserString(DocKeys.Wall)));

      if (go.Get() != Rhino.Input.GetResult.Object) return Result.Cancel;

      var wall = StratumDoc.WallOf(doc, go.Object(0).Object());
      if (wall == null)
      {
        RhinoApp.WriteLine("Stratum: that object is not part of a Stratum wall.");
        return Result.Failure;
      }

      var assembly = model.AssemblyOf(wall);
      if (assembly == null) return Result.Failure;

      double i2m = Units.InchToModel(doc);
      var baseline = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);

      double exteriorOffset, interiorOffset;
      WallSolver.FaceOffsets(assembly, wall.Justification, wall.Flipped, i2m,
                             out exteriorOffset, out interiorOffset);

      // ---- placement -------------------------------------------------------
      var widthOption = new OptionDouble(_width, true, 0.0);
      var heightOption = new OptionDouble(_height, true, 0.0);
      var sillOption = new OptionDouble(_sill, false, 0.0);
      var clearanceOption = new OptionDouble(_clearance, false, 0.0);

      // Pick from the catalog, so the opening is an instance of a real type that
      // schedules and can carry your own block geometry.
      var units = model.Catalog.OpeningUnits.ToList();
      int unitIndex = Math.Max(0, units.FindIndex(u => u.Id == _unitId));
      if (units.Count > 0)
      {
        var chosen = units[Math.Min(unitIndex, units.Count - 1)];
        _unitId = chosen.Id;
        _kind = (int)chosen.Kind;
        widthOption.CurrentValue = chosen.WidthIn;
        heightOption.CurrentValue = chosen.HeightIn;
        clearanceOption.CurrentValue = chosen.RoughClearanceIn;
        if (chosen.Kind == OpeningKind.Door) sillOption.CurrentValue = 0.0;
      }

      var gp = new GetPoint();
      gp.SetCommandPrompt("Centre of the opening (all sizes in inches)");
      gp.Constrain(baseline, false);

      int optUnit = units.Count > 0
        ? gp.AddOptionList("Unit", units.Select(u => CommandUtil.Sanitize(u.Name)).ToArray(),
                           Math.Min(unitIndex, units.Count - 1))
        : -1;
      int optKind = gp.AddOptionList("Type", KindNames, _kind);
      gp.AddOptionDouble("Width", ref widthOption);
      gp.AddOptionDouble("Height", ref heightOption);
      gp.AddOptionDouble("SillHeight", ref sillOption);
      gp.AddOptionDouble("RoughClearance", ref clearanceOption);

      gp.DynamicDraw += (sender, e) =>
      {
        double station = WallSolver.StationOfPoint(baseline, e.CurrentPoint);
        var preview = new Opening
        {
          Kind = (OpeningKind)_kind,
          StationAlongWall = station,
          WidthIn = widthOption.CurrentValue,
          HeightIn = heightOption.CurrentValue,
          SillHeightIn = sillOption.CurrentValue,
          RoughClearanceIn = clearanceOption.CurrentValue
        };
        DrawOpeningPreview(e.Display, doc, wall, preview, exteriorOffset, interiorOffset);
      };

      while (true)
      {
        var result = gp.Get();

        if (result == Rhino.Input.GetResult.Option)
        {
          var option = gp.Option();
          if (option == null) continue;

          if (option.Index == optUnit && units.Count > 0)
          {
            // Choosing a type sets the sizes; they stay editable for a one-off.
            var chosen = units[Math.Max(0, Math.Min(units.Count - 1, option.CurrentListOptionIndex))];
            _unitId = chosen.Id;
            _kind = (int)chosen.Kind;
            widthOption.CurrentValue = chosen.WidthIn;
            heightOption.CurrentValue = chosen.HeightIn;
            clearanceOption.CurrentValue = chosen.RoughClearanceIn;
            if (chosen.Kind == OpeningKind.Door) sillOption.CurrentValue = 0.0;
          }
          else if (option.Index == optKind)
          {
            _kind = option.CurrentListOptionIndex;
            _unitId = Guid.Empty;          // no longer an instance of the chosen type
            if ((OpeningKind)_kind == OpeningKind.Door)
            {
              sillOption.CurrentValue = 0.0;
              if (Math.Abs(heightOption.CurrentValue - 48.0) < 1e-9) heightOption.CurrentValue = 80.0;
            }
          }
          continue;
        }

        if (result != Rhino.Input.GetResult.Point) return Result.Cancel;
        break;
      }

      _width = widthOption.CurrentValue;
      _height = heightOption.CurrentValue;
      _sill = sillOption.CurrentValue;
      _clearance = clearanceOption.CurrentValue;

      var unit = model.Catalog.FindUnit(_unitId);
      var opening = new Opening
      {
        WallId = wall.Id,
        UnitId = _unitId,
        Kind = (OpeningKind)_kind,
        Name = NextName(model, (OpeningKind)_kind, unit),
        StationAlongWall = WallSolver.StationOfPoint(baseline, gp.Point()),
        WidthIn = _width,
        HeightIn = _height,
        SillHeightIn = _sill,
        RoughClearanceIn = _clearance
      };

      uint undoRecord = doc.BeginUndoRecord("BimOpening");
      try
      {
        wall.Openings.Add(opening);
        List<string> warnings;
        WallBaker.Rebuild(doc, model, wall, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();

      // RhinoApp.WriteLine tops out at three format arguments, so compose first.
      RhinoApp.WriteLine(string.Format(
        "Stratum: {0} '{1}' inserted - {2} x {3} R.O., sill {4}.",
        opening.Kind, opening.Name,
        Units.FormatInches(doc, opening.WidthIn + opening.RoughClearanceIn),
        Units.FormatInches(doc, opening.HeightIn + opening.RoughClearanceIn),
        Units.FormatInches(doc, opening.SillHeightEffectiveIn)));

      return Result.Success;
    }

    static string NextName(BimModel model, OpeningKind kind, OpeningUnit unit)
    {
      string prefix = unit != null && !string.IsNullOrWhiteSpace(unit.MarkPrefix)
        ? unit.MarkPrefix
        : kind == OpeningKind.Door ? "D" : kind == OpeningKind.Window ? "W" : "O";

      int count = model.Walls.SelectMany(w => w.Openings).Count(o => o.Kind == kind) + 1;
      return string.Format(CultureInfo.InvariantCulture, "{0}-{1:00}", prefix, count);
    }

    /// <summary>Cheap, instant preview: the rough opening on both wall faces plus
    /// the reveal edges between them. Booleans are far too slow to run on every
    /// mouse move, and this shows the same information.</summary>
    static void DrawOpeningPreview(Rhino.Display.DisplayPipeline display, RhinoDoc doc,
                                   WallDefinition wall, Opening opening,
                                   double exteriorOffset, double interiorOffset)
    {
      var outer = OpeningCutter.RoughOpeningOutline(doc, wall, opening, exteriorOffset);
      var inner = OpeningCutter.RoughOpeningOutline(doc, wall, opening, interiorOffset);
      if (outer == null || inner == null) return;

      var accent = Color.FromArgb(255, 235, 120, 40);
      display.DrawCurve(outer, accent, 3);
      display.DrawCurve(inner, accent, 2);

      var outerCorners = CornersOf(outer);
      var innerCorners = CornersOf(inner);
      if (outerCorners.Count == innerCorners.Count)
        for (int i = 0; i < outerCorners.Count; i++)
          display.DrawLine(outerCorners[i], innerCorners[i], accent, 1);
    }

    static List<Point3d> CornersOf(Curve rectangle)
    {
      var points = new List<Point3d>();
      Polyline polyline;
      if (rectangle.TryGetPolyline(out polyline))
        for (int i = 0; i < polyline.Count - 1; i++) points.Add(polyline[i]);
      return points;
    }
  }
}
