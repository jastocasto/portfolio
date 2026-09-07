using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>One built layer of a wall.</summary>
  public class WallLayerSolid
  {
    public int LayerIndex;
    public AssemblyLayer Layer;
    public MaterialProduct Product;
    public Brep Brep;
    public double LowOffset;
    public double HighOffset;
  }

  public class WallBuildResult
  {
    public List<WallLayerSolid> Layers = new List<WallLayerSolid>();
    public List<string> Warnings = new List<string>();
    public double GrossFaceAreaSqFt;
    public double OpeningAreaSqFt;
    public double NetFaceAreaSqFt => Math.Max(0.0, GrossFaceAreaSqFt - OpeningAreaSqFt);
    public bool Success => Layers.Count > 0;
  }

  /// <summary>
  /// Builds the actual solids. Every enabled layer of the assembly becomes one
  /// closed polysurface, positioned by <see cref="WallSolver"/>, mitred at wall
  /// joints, and cut individually at every opening according to that layer's own
  /// jamb / head / sill resolution.
  ///
  /// Nothing here touches the Rhino document - see <c>WallBaker</c> for that.
  /// This class is therefore safe to call from a dynamic draw handler, which is
  /// exactly what makes the live preview show the real wall while it is drawn.
  /// </summary>
  public static class WallBuilder
  {
    public static WallBuildResult Build(RhinoDoc doc, BimModel model, WallDefinition wall,
                                        WallJunctions junctions,
                                        bool includeOpenings = true)
    {
      var result = new WallBuildResult();
      if (doc == null || model == null || wall == null) return result;
      if (junctions == null) junctions = WallJunctions.None;

      var startJoint = junctions.Start;
      var endJoint = junctions.End;

      var assembly = model.AssemblyOf(wall);
      if (assembly == null)
      {
        result.Warnings.Add("Wall has no assembly assigned.");
        return result;
      }
      if (wall.Baseline == null || !wall.Baseline.IsValid)
      {
        result.Warnings.Add("Wall has no valid baseline.");
        return result;
      }

      double tol = doc.ModelAbsoluteTolerance;
      double inchToModel = Units.InchToModel(doc);

      // Levels are resolved first, so a floor-to-floor change reaches the geometry.
      wall.Resolve(model);

      // A wall capped against a roof is built past it and then cut, so it needs a
      // taller blank than its nominal height.
      Brep capSurface = null;
      double height = Math.Abs(wall.Height);

      if (wall.TopMode == WallTopMode.ToSurface)
      {
        capSurface = FindCapSurface(doc, wall.TopSurfaceObjectId);
        if (capSurface == null)
          result.Warnings.Add("The surface this wall caps against is missing; " +
                              "it has been built to its nominal height instead.");
        else
          height = Math.Max(height, CapBlankHeight(capSurface, wall.BaseElevation, tol));
      }

      if (height <= tol)
      {
        result.Warnings.Add("Wall height is zero.");
        return result;
      }

      var baseline = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);
      if (baseline == null) return result;

      // Run the baseline past its ends so mitres have material to cut back.
      double extStart = startJoint.Active ? Math.Max(0.0, startJoint.Extension) : 0.0;
      double extEnd = endJoint.Active ? Math.Max(0.0, endJoint.Extension) : 0.0;
      var workingCurve = Extend(baseline, extStart, extEnd);

      var ranges = WallSolver.LayerRanges(assembly, wall.Justification, wall.Flipped, inchToModel);
      if (ranges.Count == 0)
      {
        result.Warnings.Add("Assembly '" + assembly.Code + "' has no layers with thickness.");
        return result;
      }

      var openings = includeOpenings
        ? wall.Openings.Where(o => o != null).ToList()
        : new List<Opening>();

      foreach (var range in ranges)
      {
        var layer = assembly.Layers[range.Index];
        var brep = MakeLayerSolid(workingCurve, range.Low, range.High,
                                  wall.BaseElevation, height, tol);

        if (brep == null)
        {
          result.Warnings.Add("Layer '" + layer.ProductName + "' could not be built.");
          continue;
        }

        // A mitre cuts every layer on one plane. A tee does not: the core runs
        // through to the other wall's structure while the layers around it stop
        // at its face, which is what "tie to structure" means in geometry.
        bool isCore = layer.IsCore || range.Index == assembly.CoreIndex;
        brep = ApplyJoint(brep, startJoint, isCore, tol);
        brep = ApplyJoint(brep, endJoint, isCore, tol);
        if (brep == null)
        {
          result.Warnings.Add("Joint failed on layer '" + layer.ProductName + "'.");
          continue;
        }

        // Rake the layer to the roof. Done per layer, so the layers stay separate
        // all the way up the slope - which is exactly where a section through the
        // top plate has to read correctly.
        if (capSurface != null)
        {
          var capped = CapToSurface(brep, capSurface, wall.BaseElevation, tol);
          if (capped != null) brep = capped;
          else result.Warnings.Add("Layer '" + layer.ProductName + "' could not be cut to the " +
                                   "capping surface. Check that the surface passes right " +
                                   "through the wall.");
        }

        // Bites taken out of this wall where other walls die into its side.
        foreach (var notch in junctions.Notches)
        {
          if (!notch.Touches(range.Low, range.High, tol)) continue;

          var cutter = BuildNotchCutter(workingCurve, notch, range, extStart,
                                        wall.BaseElevation, height, tol);
          if (cutter == null) continue;

          var notched = Brep.CreateBooleanDifference(new[] { brep }, new[] { cutter }, tol);
          if (notched != null && notched.Length > 0)
            brep = notched.Length == 1 ? notched[0]
                 : Brep.JoinBreps(notched, tol)?.FirstOrDefault() ?? notched[0];
          else
            result.Warnings.Add("A wall meeting '" + (wall.Name ?? wall.GroupName) +
                                "' could not be notched into layer '" + layer.ProductName + "'.");
        }

        if (openings.Count > 0 && layer.CutAtOpenings)
        {
          var cutters = new List<Brep>();
          foreach (var opening in openings)
          {
            var cutter = OpeningCutter.BuildCutter(doc, wall, assembly, layer, opening,
                                                   workingCurve, extStart, range, height, tol);
            if (cutter != null) cutters.Add(cutter);
          }

          if (cutters.Count > 0)
          {
            var cut = Brep.CreateBooleanDifference(new[] { brep }, cutters, tol);
            if (cut != null && cut.Length > 0)
              brep = cut.Length == 1 ? cut[0] : Brep.JoinBreps(cut, tol)?.FirstOrDefault() ?? cut[0];
            else
              result.Warnings.Add("Opening did not cut layer '" + layer.ProductName +
                                  "'. Check that the opening sits inside the wall.");
          }
        }

        result.Layers.Add(new WallLayerSolid
        {
          LayerIndex = range.Index,
          Layer = layer,
          Product = model.Catalog.FindProduct(layer.ProductId),
          Brep = brep,
          LowOffset = range.Low,
          HighOffset = range.High
        });
      }

      double toInch = Units.ModelToInch(doc);
      result.GrossFaceAreaSqFt = (baseline.GetLength() * toInch) * (height * toInch) / 144.0;
      result.OpeningAreaSqFt = openings.Sum(o => (o.WidthIn * Math.Max(0.0, o.HeightIn)) / 144.0);

      return result;
    }

    /// <summary>Convenience overload for previews and one-off builds.</summary>
    public static WallBuildResult Build(RhinoDoc doc, BimModel model, WallDefinition wall)
      => Build(doc, model, wall, WallJunctions.None);

    // ------------------------------------------------------------------------

    /// <summary>Reads the capping geometry out of the document, as a brep.</summary>
    static Brep FindCapSurface(RhinoDoc doc, Guid objectId)
    {
      if (doc == null || objectId == Guid.Empty) return null;

      var obj = doc.Objects.FindId(objectId);
      if (obj == null || obj.IsDeleted) return null;

      var geometry = obj.Geometry;

      var brep = geometry as Brep;
      if (brep != null) return brep.DuplicateBrep();

      var surface = geometry as Surface;
      if (surface != null) return Brep.CreateFromSurface(surface);

      var extrusion = geometry as Extrusion;
      if (extrusion != null) return extrusion.ToBrep();

      var mesh = geometry as Mesh;
      if (mesh != null) return Brep.CreateFromMesh(mesh, true);

      return null;
    }

    /// <summary>How tall a blank has to be for the capping surface to pass through it.</summary>
    static double CapBlankHeight(Brep capSurface, double baseElevation, double tol)
    {
      var box = capSurface.GetBoundingBox(true);
      double margin = Math.Max(tol * 100.0, (box.Max.Z - box.Min.Z) * 0.1 + 1.0);
      return Math.Max(tol, (box.Max.Z - baseElevation) + margin);
    }

    /// <summary>
    /// Cuts one layer solid off against the capping surface, keeping what is below.
    ///
    /// Split rather than Trim: splitting a closed solid with a surface that passes
    /// right through it yields closed pieces, where trimming would leave the cut end
    /// open. The pieces to keep are the ones still sitting on the wall's base, which
    /// is a test that does not care which way the roof surface happens to be oriented.
    /// </summary>
    static Brep CapToSurface(Brep brep, Brep capSurface, double baseElevation, double tol)
    {
      if (brep == null || capSurface == null) return null;

      Brep[] pieces = null;
      try { pieces = brep.Split(capSurface, tol); }
      catch { pieces = null; }

      // No split means the surface misses the wall entirely. A wall that is wholly
      // below the roof is already correct, so leave it be.
      if (pieces == null || pieces.Length == 0)
      {
        var box = brep.GetBoundingBox(true);
        var capBox = capSurface.GetBoundingBox(true);
        return box.Max.Z <= capBox.Min.Z + tol ? brep : null;
      }

      double baseline = baseElevation + tol * 10.0;
      var kept = pieces.Where(p =>
      {
        if (p == null || !p.IsValid) return false;
        return p.GetBoundingBox(true).Min.Z <= baseline;
      }).ToList();

      if (kept.Count == 0) return null;
      if (kept.Count == 1) return Close(kept[0], tol);

      var joined = Brep.JoinBreps(kept, tol);
      return Close((joined != null && joined.Length > 0) ? joined[0] : kept[0], tol);
    }

    static Brep Close(Brep brep, double tol)
    {
      if (brep == null || brep.IsSolid) return brep;
      return brep.CapPlanarHoles(tol) ?? brep;
    }

    static Curve Extend(Curve curve, double atStart, double atEnd)
    {
      var c = curve.DuplicateCurve();
      if (c.IsClosed) return c;

      if (atStart > RhinoMath.ZeroTolerance)
        c = c.Extend(CurveEnd.Start, atStart, CurveExtensionStyle.Line) ?? c;
      if (atEnd > RhinoMath.ZeroTolerance)
        c = c.Extend(CurveEnd.End, atEnd, CurveExtensionStyle.Line) ?? c;

      return c;
    }

    static Brep ApplyJoint(Brep brep, WallJoint joint, bool isCore, double tol)
    {
      if (brep == null || !joint.Active) return brep;

      var plane = joint.PlaneFor(isCore);
      if (!plane.IsValid) return brep;

      Brep[] pieces = null;
      try { pieces = brep.Trim(plane, tol); }
      catch { pieces = null; }

      if (pieces == null || pieces.Length == 0) return brep;   // nothing to cut - leave it

      var trimmed = pieces.Length == 1 ? pieces[0] : Brep.JoinBreps(pieces, tol)?.FirstOrDefault() ?? pieces[0];
      trimmed?.Faces.SplitKinkyFaces(RhinoMath.DefaultAngleTolerance);
      var capped = trimmed != null && !trimmed.IsSolid ? trimmed.CapPlanarHoles(tol) : trimmed;
      return capped ?? trimmed ?? brep;
    }

    /// <summary>
    /// Builds one layer solid between two signed offsets from the baseline.
    /// Handles open baselines (the normal case) and closed baselines (a wall that
    /// loops back on itself) with the same call.
    /// </summary>
    public static Brep MakeLayerSolid(Curve baseline, double lowOffset, double highOffset,
                                      double baseElevation, double height, double tol)
    {
      if (baseline == null) return null;
      if (Math.Abs(highOffset - lowOffset) <= tol) return null;

      var inner = WallSolver.Offset(baseline, lowOffset, tol);
      var outer = WallSolver.Offset(baseline, highOffset, tol);
      if (inner == null || outer == null) return null;

      inner = WallSolver.Flatten(inner, baseElevation);
      outer = WallSolver.Flatten(outer, baseElevation);

      if (baseline.IsClosed)
      {
        // Ring wall: extrude both loops and subtract the smaller one.
        var outerSolid = ExtrudeClosed(outer, height, 0.0);
        var innerSolid = ExtrudeClosed(inner, height, tol * 10.0);
        if (outerSolid == null) return null;
        if (innerSolid == null) return outerSolid;

        // Whichever loop encloses more area is the outside of the ring.
        double aOuter = AreaOf(outer);
        double aInner = AreaOf(inner);
        var big = aOuter >= aInner ? outerSolid : innerSolid;
        var small = aOuter >= aInner ? innerSolid : outerSolid;

        var diff = Brep.CreateBooleanDifference(new[] { big }, new[] { small }, tol);
        return (diff != null && diff.Length > 0) ? diff[0] : big;
      }

      var profile = BuildProfile(inner, outer, tol);
      if (profile == null) return null;

      return ExtrudeClosed(profile, height, 0.0);
    }

    static double AreaOf(Curve closedCurve)
    {
      var props = AreaMassProperties.Compute(closedCurve);
      return props?.Area ?? 0.0;
    }

    /// <summary>Stitches two offset curves plus end caps into one closed planar profile.</summary>
    static Curve BuildProfile(Curve sideA, Curve sideB, double tol)
    {
      var b = sideB.DuplicateCurve();
      b.Reverse();

      var segments = new List<Curve> { sideA };

      if (sideA.PointAtEnd.DistanceTo(b.PointAtStart) > tol)
        segments.Add(new LineCurve(sideA.PointAtEnd, b.PointAtStart));

      segments.Add(b);

      if (b.PointAtEnd.DistanceTo(sideA.PointAtStart) > tol)
        segments.Add(new LineCurve(b.PointAtEnd, sideA.PointAtStart));

      var joined = Curve.JoinCurves(segments.ToArray(), tol * 2.0);
      if (joined == null || joined.Length == 0) return null;

      var candidate = joined.OrderByDescending(c => c.GetLength()).First();
      if (!candidate.IsClosed) candidate.MakeClosed(tol * 10.0);

      return candidate.IsClosed ? candidate : null;
    }

    /// <summary>
    /// The solid that removes a wall's material where another wall dies into it.
    /// Built from the same routine as the layer solids, so it lands exactly on the
    /// offsets the layers were generated from.
    /// </summary>
    static Brep BuildNotchCutter(Curve workingCurve, WallNotch notch, LayerRange range,
                                 double startExtension, double baseElevation,
                                 double height, double tol)
    {
      double bleed = Math.Max(tol * 10.0, 1e-6);

      double centre = notch.Station + startExtension;
      double half = Math.Max(tol, notch.Width * 0.5);

      double length = workingCurve.GetLength();
      double s0 = Math.Max(0.0, Math.Min(length, centre - half));
      double s1 = Math.Max(0.0, Math.Min(length, centre + half));
      if (s1 - s0 <= tol) return null;

      double t0, t1;
      if (!workingCurve.LengthParameter(s0, out t0)) return null;
      if (!workingCurve.LengthParameter(s1, out t1)) return null;

      var slice = workingCurve.Trim(t0, t1);
      if (slice == null || !slice.IsValid) return null;

      // Bleed through the thickness and vertically, never along the wall: the
      // notch cheeks are real faces that the arriving wall lands against.
      return MakeLayerSolid(slice,
                            range.Low - bleed,
                            range.High + bleed,
                            baseElevation - bleed,
                            height + bleed * 2.0,
                            tol);
    }

    /// <summary>Extrudes a closed planar curve upward into a capped solid.</summary>
    static Brep ExtrudeClosed(Curve closedCurve, double height, double overshoot)
    {
      if (closedCurve == null || !closedCurve.IsClosed || !closedCurve.IsPlanar()) return null;

      var profile = closedCurve.DuplicateCurve();
      if (overshoot > 0.0) profile.Translate(0, 0, -overshoot);

      // Extrusion.Create walks the curve plane normal, so a clockwise profile
      // would extrude downward. Force counter-clockwise about world Z.
      if (profile.ClosedCurveOrientation(Vector3d.ZAxis) == CurveOrientation.Clockwise)
        profile.Reverse();

      double h = height + overshoot * 2.0;
      var extrusion = Extrusion.Create(profile, h, true);
      if (extrusion == null) return null;

      var brep = extrusion.ToBrep();
      if (brep == null) return null;
      if (!brep.IsSolid) brep = brep.CapPlanarHoles(RhinoMath.SqrtEpsilon) ?? brep;
      return brep.IsValid ? brep : null;
    }
  }
}
