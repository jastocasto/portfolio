using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>
  /// Works out how walls meet. Where exactly two wall baselines share an end
  /// point, both walls are run past the corner and cut back on the angle
  /// bisector, so every layer mitres cleanly and no layer is left hanging in
  /// space or doubled up inside the corner.
  ///
  /// Where three or more walls meet, or where a wall ends on the middle of
  /// another, no mitre is applied: the wall is left square, which is what a
  /// detailer wants to resolve by hand anyway.
  /// </summary>
  public static class WallJoiner
  {
    class EndRef
    {
      public WallDefinition Wall;
      public bool AtStart;
      public Point3d Point;
      /// <summary>Unit tangent pointing AWAY from the corner, along the wall.</summary>
      public Vector3d Outward;
      public double ThicknessModel;
    }

    /// <summary>Computes the joints for every wall in the model in one pass.</summary>
    public static Dictionary<Guid, WallJoint[]> Solve(RhinoDoc doc, BimModel model)
    {
      var joints = new Dictionary<Guid, WallJoint[]>();
      if (doc == null || model == null) return joints;

      double tol = doc.ModelAbsoluteTolerance;
      double i2m = Units.InchToModel(doc);
      double snap = Math.Max(tol * 10.0, 0.5 * i2m);   // corners within 1/2" count as touching

      var ends = new List<EndRef>();

      foreach (var wall in model.Walls)
      {
        joints[wall.Id] = new[] { WallJoint.None, WallJoint.None };

        var assembly = model.AssemblyOf(wall);
        if (wall.Baseline == null || assembly == null) continue;
        if (wall.Baseline.IsClosed) continue;

        var flat = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);
        double thickness = assembly.TotalThicknessIn * i2m;

        var startTangent = flat.TangentAtStart; startTangent.Z = 0; startTangent.Unitize();
        var endTangent = flat.TangentAtEnd; endTangent.Z = 0; endTangent.Unitize();

        ends.Add(new EndRef
        {
          Wall = wall, AtStart = true, Point = flat.PointAtStart,
          Outward = startTangent, ThicknessModel = thickness
        });
        ends.Add(new EndRef
        {
          Wall = wall, AtStart = false, Point = flat.PointAtEnd,
          Outward = -endTangent, ThicknessModel = thickness
        });
      }

      // Group ends that land on the same point.
      var used = new bool[ends.Count];
      for (int i = 0; i < ends.Count; i++)
      {
        if (used[i]) continue;
        var cluster = new List<int> { i };
        for (int j = i + 1; j < ends.Count; j++)
        {
          if (used[j]) continue;
          if (ends[i].Point.DistanceTo(ends[j].Point) <= snap) cluster.Add(j);
        }
        foreach (var k in cluster) used[k] = true;

        if (cluster.Count != 2) continue;                       // only clean corners mitre
        var a = ends[cluster[0]];
        var b = ends[cluster[1]];
        if (a.Wall.Id == b.Wall.Id) continue;                    // a wall closing on itself

        WallJoint ja, jb;
        if (!Miter(a, b, tol, out ja, out jb)) continue;

        joints[a.Wall.Id][a.AtStart ? 0 : 1] = ja;
        joints[b.Wall.Id][b.AtStart ? 0 : 1] = jb;
      }

      return joints;
    }

    static bool Miter(EndRef a, EndRef b, double tol, out WallJoint ja, out WallJoint jb)
    {
      ja = WallJoint.None;
      jb = WallJoint.None;

      var ta = a.Outward; var tb = b.Outward;
      if (!ta.IsValid || !tb.IsValid) return false;

      double dot = Math.Max(-1.0, Math.Min(1.0, ta * tb));

      // Collinear continuation: nothing to mitre, the walls simply run on.
      if (dot < -0.9995) return false;
      // Doubled back on itself: not a corner we can resolve.
      if (dot > 0.9995) return false;

      var bisector = ta + tb;
      if (!bisector.Unitize()) return false;

      var normal = Vector3d.CrossProduct(Vector3d.ZAxis, bisector);
      if (!normal.Unitize()) return false;

      var origin = 0.5 * (new Vector3d(a.Point) + new Vector3d(b.Point));
      var corner = new Point3d(origin);

      // Orient each plane so the wall's own body is on the kept (negative) side.
      var na = (normal * ta) > 0 ? -normal : normal;
      var nb = (normal * tb) > 0 ? -normal : normal;

      // Half angle between the walls decides how far past the corner each wall
      // must run for the mitre to reach the far face.
      double angle = Math.Acos(dot);                 // 0..pi between outward tangents
      double half = Math.Max(0.05, angle * 0.5);
      double reachA = a.ThicknessModel / Math.Max(0.1, Math.Tan(half));
      double reachB = b.ThicknessModel / Math.Max(0.1, Math.Tan(half));

      ja = new WallJoint
      {
        Active = true,
        MiterPlane = new Plane(corner, na),
        Extension = Math.Min(reachA, a.ThicknessModel * 20.0) + a.ThicknessModel
      };
      jb = new WallJoint
      {
        Active = true,
        MiterPlane = new Plane(corner, nb),
        Extension = Math.Min(reachB, b.ThicknessModel * 20.0) + b.ThicknessModel
      };
      return true;
    }

    /// <summary>Joints for a single wall, for previews and one-off rebuilds.</summary>
    public static WallJoint[] SolveFor(RhinoDoc doc, BimModel model, WallDefinition wall)
    {
      var all = Solve(doc, model);
      WallJoint[] result;
      return all.TryGetValue(wall?.Id ?? Guid.Empty, out result)
        ? result
        : new[] { WallJoint.None, WallJoint.None };
    }
  }
}
