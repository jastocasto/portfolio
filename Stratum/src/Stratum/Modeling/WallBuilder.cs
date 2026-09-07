using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>A joint at one end of a wall, produced by <see cref="WallJoiner"/>.</summary>
  public struct WallJoint
  {
    public bool Active;
    /// <summary>Mitre plane. The wall body is kept on the negative side.</summary>
    public Plane MiterPlane;
    /// <summary>How far the baseline must run past its end for the mitre to bite.</summary>
    public double Extension;

    public static WallJoint None => new WallJoint { Active = false, Extension = 0.0 };
  }

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
                                        WallJoint startJoint, WallJoint endJoint,
                                        bool includeOpenings = true)
    {
      var result = new WallBuildResult();
      if (doc == null || model == null || wall == null) return result;

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
      double height = Math.Abs(wall.Height);
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

        brep = ApplyMiter(brep, startJoint, tol);
        brep = ApplyMiter(brep, endJoint, tol);
        if (brep == null)
        {
          result.Warnings.Add("Mitre failed on layer '" + layer.ProductName + "'.");
          continue;
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
      => Build(doc, model, wall, WallJoint.None, WallJoint.None);

    // ------------------------------------------------------------------------

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

    static Brep ApplyMiter(Brep brep, WallJoint joint, double tol)
    {
      if (brep == null || !joint.Active || !joint.MiterPlane.IsValid) return brep;

      Brep[] pieces = null;
      try { pieces = brep.Trim(joint.MiterPlane, tol); }
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
