using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Modeling;

namespace Stratum.Commands
{
  /// <summary>
  /// Draws walls. Pick points and the full layered assembly is drawn in 3-D under
  /// the cursor as you go - every layer, in its real position and thickness, with
  /// the reference line where the justification says it is.
  ///
  /// Each span between two picked points becomes its own wall, exactly as it does
  /// in a BIM authoring tool, so corners mitre and each run can be edited,
  /// re-typed or deleted on its own.
  /// </summary>
  [Guid("d9ebaf9b-a17a-49dd-b78b-61d32fb5be34")]
  public class BimWallCommand : Command
  {
    public BimWallCommand() { Instance = this; }
    public static BimWallCommand Instance { get; private set; }

    public override string EnglishName => "BimWall";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var codes = CommandUtil.AssemblyCodes(model);
      int assemblyIndex = Math.Max(0, model.Catalog.Assemblies.FindIndex(a => a.Id == model.ActiveAssemblyId));
      int justIndex = (int)model.ActiveJustification;
      bool flipped = false;

      double baseElevation = ActiveElevation(doc);
      var heightOption = new OptionDouble(model.ActiveHeight, true, 0.0);
      var baseOption = new OptionDouble(baseElevation);

      RhinoApp.WriteLine("Stratum: " + CommandUtil.Describe(doc, model, model.Catalog.Assemblies[assemblyIndex]));

      var placed = new List<Point3d>();
      var created = new List<WallDefinition>();
      bool switchToCurves = false;
      uint undoRecord = doc.BeginUndoRecord("BimWall");

      try
      {
        while (true)
        {
          var gp = new GetPoint();
          bool first = placed.Count == 0;

          gp.SetCommandPrompt(first ? "Start of wall" : "Next point of wall");
          gp.Constrain(new Plane(new Point3d(0, 0, baseElevation), Vector3d.ZAxis), false);
          gp.AcceptNothing(!first);

          int optAssembly = gp.AddOptionList("Assembly", codes, assemblyIndex);
          int optJust = gp.AddOptionList("Justification", CommandUtil.JustificationNames, justIndex);
          gp.AddOptionDouble("Height", ref heightOption);
          gp.AddOptionDouble("BaseElevation", ref baseOption);
          int optFlip = gp.AddOption("Flip");
          int optCurves = first ? gp.AddOption("FromCurves") : -1;
          int optUndo = first ? -1 : gp.AddOption("Undo");
          int optClose = placed.Count > 2 ? gp.AddOption("Close") : -1;

          if (!first)
          {
            gp.SetBasePoint(placed[placed.Count - 1], true);
            gp.DrawLineFromPoint(placed[placed.Count - 1], true);
          }

          // ---- live 3-D preview of the wall being drawn ---------------------
          var previewFrom = first ? Point3d.Unset : placed[placed.Count - 1];
          int previewAssembly = assemblyIndex;
          int previewJust = justIndex;
          bool previewFlip = flipped;

          gp.DynamicDraw += (sender, e) =>
          {
            if (!previewFrom.IsValid) return;
            var line = new LineCurve(previewFrom, e.CurrentPoint);
            if (line.GetLength() <= doc.ModelAbsoluteTolerance) return;

            var build = CommandUtil.PreviewWall(doc, model, line,
              model.Catalog.Assemblies[previewAssembly].Id,
              baseOption.CurrentValue, heightOption.CurrentValue,
              (WallJustification)previewJust, previewFlip);

            CommandUtil.DrawPreview(e.Display, build, line);
          };

          var result = gp.Get();

          if (result == GetResult.Option)
          {
            var option = gp.Option();
            if (option != null)
            {
              if (option.Index == optAssembly) assemblyIndex = option.CurrentListOptionIndex;
              else if (option.Index == optJust) justIndex = option.CurrentListOptionIndex;
              else if (option.Index == optFlip) flipped = !flipped;
              else if (option.Index == optClose && placed.Count > 2)
              {
                AddSegment(doc, model, created, placed[placed.Count - 1], placed[0],
                           model.Catalog.Assemblies[assemblyIndex].Id, baseOption.CurrentValue,
                           heightOption.CurrentValue, (WallJustification)justIndex, flipped);
                break;
              }
              else if (option.Index == optUndo && created.Count > 0)
              {
                var last = created[created.Count - 1];
                created.RemoveAt(created.Count - 1);
                WallBaker.DeleteWall(doc, model, last);
                if (placed.Count > 1) placed.RemoveAt(placed.Count - 1);
                doc.Views.Redraw();
              }
              else if (option.Index == optCurves)
              {
                // Leave the loop cleanly so this command's undo record closes
                // before the curve-driven pass opens its own.
                switchToCurves = true;
                break;
              }
            }

            baseElevation = baseOption.CurrentValue;
            model.ActiveHeight = heightOption.CurrentValue;
            model.ActiveAssemblyId = model.Catalog.Assemblies[assemblyIndex].Id;
            model.ActiveJustification = (WallJustification)justIndex;
            continue;
          }

          if (result == GetResult.Nothing) break;
          if (result != GetResult.Point) return Result.Cancel;   // finally closes the undo record

          var point = gp.Point();
          if (placed.Count > 0)
          {
            var previous = placed[placed.Count - 1];
            if (previous.DistanceTo(point) > doc.ModelAbsoluteTolerance)
              AddSegment(doc, model, created, previous, point,
                         model.Catalog.Assemblies[assemblyIndex].Id, baseOption.CurrentValue,
                         heightOption.CurrentValue, (WallJustification)justIndex, flipped);
          }

          placed.Add(point);
        }
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      if (switchToCurves)
        return FromCurves(doc, model, model.Catalog.Assemblies[assemblyIndex].Id,
                          heightOption.CurrentValue, (WallJustification)justIndex, flipped);

      if (created.Count == 0) return Result.Nothing;

      model.ActiveAssemblyId = model.Catalog.Assemblies[assemblyIndex].Id;
      model.ActiveJustification = (WallJustification)justIndex;
      model.ActiveHeight = heightOption.CurrentValue;

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();

      RhinoApp.WriteLine("Stratum: {0} wall{1} created.", created.Count, created.Count == 1 ? "" : "s");
      return Result.Success;
    }

    static double ActiveElevation(RhinoDoc doc)
    {
      var view = doc.Views.ActiveView;
      if (view == null) return 0.0;
      var plane = view.ActiveViewport.ConstructionPlane();
      return plane.Origin.Z;
    }

    static void AddSegment(RhinoDoc doc, BimModel model, List<WallDefinition> created,
                           Point3d from, Point3d to, Guid assemblyId, double baseElevation,
                           double height, WallJustification justification, bool flipped)
    {
      var wall = new WallDefinition
      {
        AssemblyId = assemblyId,
        Baseline = new LineCurve(from, to),
        BaseElevation = baseElevation,
        Height = height,
        Justification = justification,
        Flipped = flipped
      };

      model.Walls.Add(wall);
      created.Add(wall);

      List<string> warnings;
      WallBaker.Rebuild(doc, model, wall, out warnings);
      CommandUtil.ReportWarnings(warnings);
    }

    /// <summary>Builds walls along curves that already exist in the model - the
    /// usual route when the plan arrives as a linework import.</summary>
    static Result FromCurves(RhinoDoc doc, BimModel model, Guid assemblyId, double height,
                             WallJustification justification, bool flipped)
    {
      var go = new GetObject();
      go.SetCommandPrompt("Select curves to build walls along");
      go.GeometryFilter = Rhino.DocObjects.ObjectType.Curve;
      go.SubObjectSelect = false;
      go.EnablePreSelect(true, true);
      go.GetMultiple(1, 0);

      if (go.CommandResult() != Result.Success) return go.CommandResult();

      uint undoRecord = doc.BeginUndoRecord("BimWall from curves");
      var created = new List<WallDefinition>();

      try
      {
        foreach (var objRef in go.Objects())
        {
          var curve = objRef?.Curve();
          if (curve == null || curve.GetLength() <= doc.ModelAbsoluteTolerance) continue;

          double elevation = curve.PointAtStart.Z;

          // Explode polylines into one wall per span so corners mitre properly.
          var spans = ExplodeToWallRuns(curve, doc.ModelAbsoluteTolerance);

          foreach (var span in spans)
          {
            var wall = new WallDefinition
            {
              AssemblyId = assemblyId,
              Baseline = span,
              BaseElevation = elevation,
              Height = height,
              Justification = justification,
              Flipped = flipped
            };
            model.Walls.Add(wall);
            created.Add(wall);
          }
        }

        List<string> warnings;
        WallBaker.RebuildMany(doc, model, created, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      RhinoApp.WriteLine("Stratum: {0} wall{1} created from curves.", created.Count, created.Count == 1 ? "" : "s");
      return created.Count > 0 ? Result.Success : Result.Nothing;
    }

    static IEnumerable<Curve> ExplodeToWallRuns(Curve curve, double tolerance)
    {
      Polyline polyline;
      if (curve.TryGetPolyline(out polyline) && polyline.Count > 2)
      {
        for (int i = 0; i < polyline.Count - 1; i++)
        {
          if (polyline[i].DistanceTo(polyline[i + 1]) > tolerance)
            yield return new LineCurve(polyline[i], polyline[i + 1]);
        }
        yield break;
      }

      var segments = curve.DuplicateSegments();
      if (segments != null && segments.Length > 1)
      {
        foreach (var segment in segments) yield return segment;
        yield break;
      }

      yield return curve.DuplicateCurve();
    }
  }
}
