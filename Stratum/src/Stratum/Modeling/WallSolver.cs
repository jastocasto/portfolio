using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>One layer's position across the wall, in model units.</summary>
  public struct LayerRange
  {
    public int Index;
    public double Low;      // signed offset from the baseline, model units
    public double High;
    public double Thickness => High - Low;
    public double Mid => 0.5 * (Low + High);
  }

  /// <summary>
  /// Turns an assembly plus a justification into signed offsets from the wall
  /// baseline, and offsets curves reliably in the horizontal plane.
  ///
  /// Sign convention: a POSITIVE offset is to the LEFT of the baseline when
  /// walking along it. Left is the exterior side unless the wall is flipped.
  /// </summary>
  public static class WallSolver
  {
    /// <summary>Signed offset of a point at station <paramref name="u"/> (inches from the
    /// exterior face) for a wall drawn with the given justification.</summary>
    public static double OffsetOfStation(WallAssembly assembly, WallJustification justification,
                                         bool flipped, double u, double inchToModel)
    {
      double u0 = assembly.BaselineStation(justification);
      double dir = flipped ? -1.0 : 1.0;
      return dir * (u0 - u) * inchToModel;
    }

    /// <summary>Signed offsets of every enabled layer, exterior first.</summary>
    public static List<LayerRange> LayerRanges(WallAssembly assembly, WallJustification justification,
                                               bool flipped, double inchToModel)
    {
      var result = new List<LayerRange>();
      if (assembly == null) return result;

      double u = 0.0;
      for (int i = 0; i < assembly.Layers.Count; i++)
      {
        var layer = assembly.Layers[i];
        if (!layer.Enabled) continue;

        double t = Math.Max(0.0, layer.ThicknessIn);
        if (t <= 1e-9) continue;

        double o1 = OffsetOfStation(assembly, justification, flipped, u, inchToModel);
        double o2 = OffsetOfStation(assembly, justification, flipped, u + t, inchToModel);

        result.Add(new LayerRange
        {
          Index = i,
          Low = Math.Min(o1, o2),
          High = Math.Max(o1, o2)
        });

        u += t;
      }
      return result;
    }

    /// <summary>Signed offsets of the two wall faces (exterior, interior).</summary>
    public static void FaceOffsets(WallAssembly assembly, WallJustification justification,
                                   bool flipped, double inchToModel,
                                   out double exterior, out double interior)
    {
      exterior = OffsetOfStation(assembly, justification, flipped, 0.0, inchToModel);
      interior = OffsetOfStation(assembly, justification, flipped, assembly.TotalThicknessIn, inchToModel);
    }

    /// <summary>Unit vector pointing to the LEFT of the curve at the given parameter.</summary>
    public static Vector3d LeftAt(Curve curve, double t)
    {
      var tangent = curve.TangentAt(t);
      tangent.Z = 0.0;
      if (!tangent.Unitize()) return Vector3d.YAxis;
      var left = Vector3d.CrossProduct(Vector3d.ZAxis, tangent);
      left.Unitize();
      return left;
    }

    public static Vector3d LeftAtMid(Curve curve)
    {
      double t;
      if (!curve.NormalizedLengthParameter(0.5, out t)) t = curve.Domain.Mid;
      return LeftAt(curve, t);
    }

    /// <summary>
    /// Offsets a curve horizontally by a signed distance. Positive is to the left.
    /// Falls back to a straight translation for the degenerate cases where
    /// Curve.Offset gives up, so a wall is never silently lost.
    /// </summary>
    public static Curve Offset(Curve curve, double signedDistance, double tolerance)
    {
      if (curve == null) return null;
      if (Math.Abs(signedDistance) <= RhinoMath.ZeroTolerance)
        return curve.DuplicateCurve();

      double t;
      if (!curve.NormalizedLengthParameter(0.5, out t)) t = curve.Domain.Mid;
      var mid = curve.PointAt(t);
      var left = LeftAt(curve, t);
      var directionPoint = mid + left * (signedDistance > 0 ? 1.0 : -1.0);

      Curve[] offsets = null;
      try
      {
        offsets = curve.Offset(directionPoint, Vector3d.ZAxis, Math.Abs(signedDistance),
                               tolerance, CurveOffsetCornerStyle.Sharp);
      }
      catch { offsets = null; }

      if (offsets != null && offsets.Length > 0)
      {
        if (offsets.Length == 1) return offsets[0];
        var joined = Curve.JoinCurves(offsets, tolerance * 2.0);
        var pool = (joined != null && joined.Length > 0) ? joined : offsets;
        return pool.OrderByDescending(c => c.GetLength()).First();
      }

      // Fallback: translate. Correct for lines, acceptable for gentle arcs.
      var copy = curve.DuplicateCurve();
      copy.Translate(left * signedDistance);
      return copy;
    }

    /// <summary>Flattens a curve onto a horizontal plane at the given elevation.</summary>
    public static Curve Flatten(Curve curve, double elevation)
    {
      if (curve == null) return null;
      var flat = curve.DuplicateCurve();

      var bbox = flat.GetBoundingBox(true);
      bool alreadyFlat = Math.Abs(bbox.Max.Z - bbox.Min.Z) < RhinoMath.ZeroTolerance;

      if (alreadyFlat)
      {
        flat.Translate(0, 0, elevation - bbox.Min.Z);
        return flat;
      }

      // Genuinely 3-D input (a curve picked off a surface, say): project it.
      var projected = Curve.ProjectToPlane(flat, new Plane(new Point3d(0, 0, elevation), Vector3d.ZAxis));
      return projected ?? flat;
    }

    /// <summary>Frame at a distance along the wall: X along the wall, Y to the left, Z up.</summary>
    public static bool FrameAtStation(Curve baseline, double station, out Plane frame)
    {
      frame = Plane.Unset;
      if (baseline == null) return false;

      double length = baseline.GetLength();
      if (length <= RhinoMath.ZeroTolerance) return false;

      station = Math.Max(0.0, Math.Min(length, station));

      double t;
      if (!baseline.LengthParameter(station, out t)) return false;

      var origin = baseline.PointAt(t);
      var tangent = baseline.TangentAt(t);
      tangent.Z = 0.0;
      if (!tangent.Unitize()) return false;

      frame = new Plane(origin, tangent, Vector3d.CrossProduct(Vector3d.ZAxis, tangent));
      return frame.IsValid;
    }

    /// <summary>Distance along the baseline of the point on it closest to a test point.</summary>
    public static double StationOfPoint(Curve baseline, Point3d testPoint)
    {
      if (baseline == null) return 0.0;
      double t;
      if (!baseline.ClosestPoint(testPoint, out t)) return 0.0;
      var sub = baseline.Trim(baseline.Domain.T0, t);
      return sub?.GetLength() ?? 0.0;
    }
  }
}
