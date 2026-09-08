using System;
using System.Collections.Generic;
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
  /// Draws a floor. Either click inside a room and let Stratum work the boundary out
  /// from the walls around it, or pick closed curves yourself.
  /// </summary>
  [Guid("385c3946-f171-48e1-b042-2a59d5e7e30e")]
  public class BimFloorCommand : Command
  {
    public BimFloorCommand() { Instance = this; }
    public static BimFloorCommand Instance { get; private set; }

    public override string EnglishName => "BimFloor";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var floorTypes = model.Catalog.Assemblies
        .Where(a => a.Kind == AssemblyKind.Floor)
        .ToList();

      if (floorTypes.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: no floor types in the catalog. " +
                           "Add one in BimAssemblies, or run BimLibrary to restore the defaults.");
        return Result.Nothing;
      }

      int typeIndex = 0;
      var level = model.LevelFor(ActiveElevation(doc)) ?? model.SortedLevels.FirstOrDefault();

      var gp = new GetPoint();
      gp.SetCommandPrompt("Click inside a room to floor it");
      int optType = gp.AddOptionList("Type",
        floorTypes.Select(a => CommandUtil.Sanitize(a.Code)).ToArray(), typeIndex);
      int optCurves = gp.AddOption("PickCurves");
      gp.AcceptNothing(false);

      var created = new List<SlabDefinition>();
      uint undo = doc.BeginUndoRecord("BimFloor");

      try
      {
        while (true)
        {
          var res = gp.Get();

          if (res == Rhino.Input.GetResult.Option)
          {
            var option = gp.Option();
            if (option == null) continue;

            if (option.Index == optType) typeIndex = option.CurrentListOptionIndex;
            else if (option.Index == optCurves)
            {
              var fromCurves = FromCurves(doc, model, floorTypes[typeIndex], level, created);
              if (fromCurves != Result.Success) return fromCurves;
              break;
            }
            continue;
          }

          if (res != Rhino.Input.GetResult.Point) break;

          var boundary = RoomBoundary(doc, model, gp.Point());
          if (boundary == null)
          {
            RhinoApp.WriteLine("Stratum: couldn't find a closed room around that point. " +
                               "Check the walls form a loop, or use PickCurves.");
            continue;
          }

          created.Add(Add(doc, model, floorTypes[typeIndex], level, boundary, null));
          RhinoApp.WriteLine("Stratum: floor added. Click another room, or press Enter.");
          gp.AcceptNothing(true);
        }

        List<string> warnings;
        RebuildAll(doc, model, created, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally { doc.EndUndoRecord(undo); }

      if (created.Count == 0) return Result.Nothing;

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      RhinoApp.WriteLine("Stratum: {0} floor{1} created.", created.Count, created.Count == 1 ? "" : "s");
      return Result.Success;
    }

    /// <summary>
    /// Works the room boundary out from the walls enclosing a point.
    ///
    /// Uses the wall baselines rather than their faces: a floor normally runs under
    /// the partitions and out to the framing, and a baseline region is far more
    /// robust than trying to stitch face curves that mitre and notch at every joint.
    /// </summary>
    static Curve RoomBoundary(RhinoDoc doc, BimModel model, Point3d inside)
    {
      double tol = doc.ModelAbsoluteTolerance;

      var lines = model.Walls
        .Where(w => w.Baseline != null)
        .Select(w => WallSolver.Flatten(w.Baseline, inside.Z))
        .Where(c => c != null)
        .ToList();

      if (lines.Count < 3) return null;

      var plane = new Plane(new Point3d(0, 0, inside.Z), Vector3d.ZAxis);

      CurveBooleanRegions regions = null;
      try
      {
        regions = Curve.CreateBooleanRegions(lines, plane, new[] { inside }, true, tol);
      }
      catch { regions = null; }

      if (regions == null || regions.RegionCount == 0) return null;

      // The point was handed in as the region seed, so the first region is the room
      // it sits in; take its outer loop.
      var curves = regions.RegionCurves(0);
      if (curves == null || curves.Length == 0) return null;

      return curves.OrderByDescending(EnclosedArea).First();
    }

    static double EnclosedArea(Curve curve)
    {
      if (curve == null || !curve.IsClosed) return 0.0;
      var props = AreaMassProperties.Compute(curve);
      return props?.Area ?? 0.0;
    }

    static Result FromCurves(RhinoDoc doc, BimModel model, LayeredAssembly type,
                             Level level, List<SlabDefinition> created)
    {
      var go = new GetObject();
      go.SetCommandPrompt("Select closed curves for the floor outline");
      go.GeometryFilter = ObjectType.Curve;
      go.SubObjectSelect = false;
      go.EnablePreSelect(true, true);
      go.SetCustomGeometryFilter((rhObject, geometry, componentIndex) =>
      {
        var c = geometry as Curve;
        return c != null && c.IsClosed && c.IsPlanar(doc.ModelAbsoluteTolerance);
      });
      go.GetMultiple(1, 0);

      if (go.CommandResult() != Result.Success) return go.CommandResult();

      foreach (var objRef in go.Objects())
      {
        var curve = objRef?.Curve();
        if (curve == null || !curve.IsClosed) continue;
        created.Add(Add(doc, model, type, level, curve.DuplicateCurve(), null));
      }

      return Result.Success;
    }

    static SlabDefinition Add(RhinoDoc doc, BimModel model, LayeredAssembly type,
                              Level level, Curve boundary, List<Curve> holes)
    {
      var slab = new SlabDefinition
      {
        AssemblyId = type.Id,
        Boundary = boundary,
        LevelId = level?.Id ?? Guid.Empty,
        Offset = 0.0,
        Elevation = level?.Elevation ?? boundary.PointAtStart.Z,
        Justification = AssemblyJustification.FirstCore
      };
      if (holes != null) slab.Holes.AddRange(holes);

      model.Slabs.Add(slab);
      return slab;
    }

    static void RebuildAll(RhinoDoc doc, BimModel model, List<SlabDefinition> slabs,
                           out List<string> warnings)
    {
      warnings = new List<string>();
      foreach (var slab in slabs) SlabBaker.RebuildSlab(doc, model, slab, warnings);
    }

    static double ActiveElevation(RhinoDoc doc)
    {
      var view = doc.Views.ActiveView;
      return view == null ? 0.0 : view.ActiveViewport.ConstructionPlane().Origin.Z;
    }
  }

  /// <summary>
  /// Builds a layered roof off a surface you drew.
  ///
  /// Point it at the same surface the walls cap against and the two agree by
  /// construction: the walls rake to exactly where the roof's underside sits.
  /// </summary>
  [Guid("566c1b10-3767-4ca2-9f93-282a3cf7e2ea")]
  public class BimRoofCommand : Command
  {
    public BimRoofCommand() { Instance = this; }
    public static BimRoofCommand Instance { get; private set; }

    public override string EnglishName => "BimRoof";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var roofTypes = model.Catalog.Assemblies
        .Where(a => a.Kind == AssemblyKind.Roof)
        .ToList();

      if (roofTypes.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: no roof types in the catalog. " +
                           "Add one in BimAssemblies, or run BimLibrary to restore the defaults.");
        return Result.Nothing;
      }

      var go = new GetObject();
      go.SetCommandPrompt("Select the surface to build the roof from");
      go.GeometryFilter = ObjectType.Surface | ObjectType.Brep | ObjectType.Extrusion;
      go.SubObjectSelect = false;
      go.EnablePreSelect(true, true);
      go.SetCustomGeometryFilter((rhObject, geometry, componentIndex) =>
        rhObject != null &&
        string.IsNullOrEmpty(rhObject.Attributes.GetUserString(DocKeys.Wall)) &&
        string.IsNullOrEmpty(rhObject.Attributes.GetUserString(DocKeys.Element)));
      go.GetMultiple(1, 0);

      if (go.CommandResult() != Result.Success) return go.CommandResult();

      int typeIndex = 0;
      var gt = new GetOption();
      gt.SetCommandPrompt("Roof type");
      int optType = gt.AddOptionList("Type",
        roofTypes.Select(a => CommandUtil.Sanitize(a.Code)).ToArray(), 0);
      gt.AcceptNothing(true);
      if (gt.Get() == Rhino.Input.GetResult.Option)
      {
        var option = gt.Option();
        if (option != null && option.Index == optType) typeIndex = option.CurrentListOptionIndex;
      }

      var created = new List<RoofDefinition>();
      uint undo = doc.BeginUndoRecord("BimRoof");

      try
      {
        var warnings = new List<string>();

        foreach (var objRef in go.Objects())
        {
          var roof = new RoofDefinition
          {
            AssemblyId = roofTypes[typeIndex].Id,
            SurfaceObjectId = objRef.ObjectId
          };
          model.Roofs.Add(roof);
          created.Add(roof);

          SlabBaker.RebuildRoof(doc, model, roof, warnings);
        }

        CommandUtil.ReportWarnings(warnings);
      }
      finally { doc.EndUndoRecord(undo); }

      if (created.Count == 0) return Result.Nothing;

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();

      RhinoApp.WriteLine("Stratum: {0} roof{1} built. The surface stays in the document - " +
                         "edit it and run BimRebuild to regenerate.",
                         created.Count, created.Count == 1 ? "" : "s");
      return Result.Success;
    }
  }
}
