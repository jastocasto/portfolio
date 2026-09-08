using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>
  /// Builds floors and roofs, one solid per material layer, the same way walls are
  /// built and with the same numbers behind it.
  ///
  /// The layer offsets come straight from <see cref="WallSolver.LayerRanges"/>. That
  /// is not a shortcut: "where does each layer sit relative to the reference" is the
  /// same question for a wall and a floor, only the axis differs. Reusing it means
  /// the justification behaviour a floor gets is the behaviour already proved for
  /// walls - the structural core stays put and the other layers grow off it.
  /// </summary>
  public static class SlabBuilder
  {
    public class LayerSolid
    {
      public int LayerIndex;
      public AssemblyLayer Layer;
      public MaterialProduct Product;
      public Brep Brep;
    }

    public class Result
    {
      public List<LayerSolid> Layers = new List<LayerSolid>();
      public List<string> Warnings = new List<string>();
      public bool Success => Layers.Count > 0;
    }

    // ------------------------------------------------------------------------
    //  Floors
    // ------------------------------------------------------------------------

    /// <summary>
    /// A floor: the boundary region extruded through each layer's thickness.
    ///
    /// Offsets are measured downward from the reference plane, so with the default
    /// "top of core" justification the level lands on top of the joists and the
    /// finish sits above it, which is how a framer reads it.
    /// </summary>
    public static Result BuildSlab(RhinoDoc doc, BimModel model, SlabDefinition slab)
    {
      var result = new Result();
      if (doc == null || model == null || slab == null) return result;

      var assembly = model.Catalog.FindAssembly(slab.AssemblyId);
      if (assembly == null)
      {
        result.Warnings.Add("Floor has no assembly assigned.");
        return result;
      }

      if (slab.Boundary == null || !slab.Boundary.IsClosed)
      {
        result.Warnings.Add("Floor needs a closed boundary curve.");
        return result;
      }

      slab.Resolve(model);

      double tol = doc.ModelAbsoluteTolerance;
      double i2m = Units.InchToModel(doc);

      var ranges = WallSolver.LayerRanges(assembly, slab.Justification, false, i2m);
      if (ranges.Count == 0)
      {
        result.Warnings.Add("Assembly '" + assembly.Code + "' has no layers with thickness.");
        return result;
      }

      // Flatten the boundary and holes onto the reference plane; they are then moved
      // to each layer's own elevation.
      var boundary = WallSolver.Flatten(slab.Boundary, slab.Elevation);
      var holes = slab.Holes.Where(h => h != null && h.IsClosed)
                            .Select(h => WallSolver.Flatten(h, slab.Elevation))
                            .Where(h => h != null)
                            .ToList();

      var face = PlanarRegion(boundary, holes, tol);
      if (face == null)
      {
        result.Warnings.Add("Could not build a planar region from the floor boundary. " +
                            "Check that it is closed and planar, and that the holes sit inside it.");
        return result;
      }

      foreach (var range in ranges)
      {
        var layer = assembly.Layers[range.Index];

        // WallSolver measures a positive offset to the "first" side. For a floor that
        // side is the top, so the layer occupies Elevation+Low .. Elevation+High.
        double thickness = range.High - range.Low;
        if (thickness <= tol) continue;

        var solid = ExtrudeRegion(face, slab.Elevation + range.Low, thickness, tol);
        if (solid == null)
        {
          result.Warnings.Add("Layer '" + layer.ProductName + "' could not be built.");
          continue;
        }

        result.Layers.Add(new LayerSolid
        {
          LayerIndex = range.Index,
          Layer = layer,
          Product = model.Catalog.FindProduct(layer.ProductId),
          Brep = solid
        });
      }

      return result;
    }

    /// <summary>The boundary and its holes as one trimmed planar face.</summary>
    static BrepFace PlanarRegion(Curve boundary, List<Curve> holes, double tol)
    {
      var curves = new List<Curve> { boundary };
      curves.AddRange(holes);

      Brep[] planar = null;
      try { planar = Brep.CreatePlanarBreps(curves, tol); }
      catch { planar = null; }

      if (planar == null || planar.Length == 0)
      {
        // The holes may be the problem; a floor with no hole is better than none.
        try { planar = Brep.CreatePlanarBreps(boundary, tol); }
        catch { planar = null; }
      }

      if (planar == null || planar.Length == 0) return null;

      var best = planar.OrderByDescending(b => b.GetArea()).First();
      return best.Faces.Count > 0 ? best.Faces[0] : null;
    }

    /// <summary>
    /// Turns a planar face into a solid of the given thickness, sitting with its
    /// underside at the given elevation. CreateFromOffsetFace carries the face's
    /// trims through, so stair wells and chases stay cut out of every layer.
    /// </summary>
    static Brep ExtrudeRegion(BrepFace face, double bottomElevation, double thickness, double tol)
    {
      Brep solid = null;
      try { solid = Brep.CreateFromOffsetFace(face, thickness, tol, false, true); }
      catch { solid = null; }

      if (solid == null) return null;

      // The face sits at the reference elevation; move the finished layer so its
      // underside lands where it belongs.
      var box = solid.GetBoundingBox(true);
      solid.Translate(0, 0, bottomElevation - box.Min.Z);

      if (!solid.IsSolid) solid = solid.CapPlanarHoles(tol) ?? solid;
      return solid.IsValid ? solid : null;
    }

    // ------------------------------------------------------------------------
    //  Roofs
    // ------------------------------------------------------------------------

    /// <summary>
    /// A roof: each layer offset off the surface you drew.
    ///
    /// The surface is taken as one face of the stack (by default the top of the
    /// structural deck), and each layer is offset to its own near face and then
    /// thickened. Offsetting a trimmed or kinked surface is the least predictable
    /// operation in Stratum, so a layer that fails is reported by name rather than
    /// quietly dropped.
    /// </summary>
    public static Result BuildRoof(RhinoDoc doc, BimModel model, RoofDefinition roof)
    {
      var result = new Result();
      if (doc == null || model == null || roof == null) return result;

      var assembly = model.Catalog.FindAssembly(roof.AssemblyId);
      if (assembly == null)
      {
        result.Warnings.Add("Roof has no assembly assigned.");
        return result;
      }

      var surface = FindSurface(doc, roof.SurfaceObjectId);
      if (surface == null)
      {
        result.Warnings.Add("The surface this roof is built from is missing. " +
                            "Re-pick it with BimRoof, or undo the deletion.");
        return result;
      }

      double tol = doc.ModelAbsoluteTolerance;
      double i2m = Units.InchToModel(doc);

      var ranges = WallSolver.LayerRanges(assembly, roof.Justification, false, i2m);
      if (ranges.Count == 0)
      {
        result.Warnings.Add("Assembly '" + assembly.Code + "' has no layers with thickness.");
        return result;
      }

      foreach (var range in ranges)
      {
        var layer = assembly.Layers[range.Index];
        double thickness = range.High - range.Low;
        if (thickness <= tol) continue;

        var solid = OffsetLayer(surface, range.Low, thickness, tol);
        if (solid == null)
        {
          result.Warnings.Add("Layer '" + layer.ProductName + "' could not be offset off the " +
                              "roof surface. Simplifying the surface, or splitting it into " +
                              "untrimmed planes, usually fixes this.");
          continue;
        }

        result.Layers.Add(new LayerSolid
        {
          LayerIndex = range.Index,
          Layer = layer,
          Product = model.Catalog.FindProduct(layer.ProductId),
          Brep = solid
        });
      }

      return result;
    }

    /// <summary>Offsets the surface to a layer's near face, then thickens it.</summary>
    static Brep OffsetLayer(Brep surface, double nearOffset, double thickness, double tol)
    {
      try
      {
        var start = surface;

        if (Math.Abs(nearOffset) > tol)
        {
          Brep[] blends, walls;
          var moved = Brep.CreateOffsetBrep(surface, nearOffset, false, true, tol, out blends, out walls);
          if (moved == null || moved.Length == 0) return null;
          start = moved[0];
        }

        Brep[] b2, w2;
        var solids = Brep.CreateOffsetBrep(start, thickness, true, true, tol, out b2, out w2);
        if (solids == null || solids.Length == 0) return null;

        var solid = solids.OrderByDescending(x => x.GetVolume()).First();
        if (!solid.IsSolid) solid = solid.CapPlanarHoles(tol) ?? solid;
        return solid.IsValid ? solid : null;
      }
      catch
      {
        return null;
      }
    }

    static Brep FindSurface(RhinoDoc doc, Guid objectId)
    {
      if (doc == null || objectId == Guid.Empty) return null;

      var obj = doc.Objects.FindId(objectId);
      if (obj == null || obj.IsDeleted) return null;

      var geometry = obj.Geometry;

      var brep = geometry as Brep;
      if (brep != null) return brep.DuplicateBrep();

      var srf = geometry as Surface;
      if (srf != null) return Brep.CreateFromSurface(srf);

      var extrusion = geometry as Extrusion;
      if (extrusion != null) return extrusion.ToBrep();

      return null;
    }
  }
}
