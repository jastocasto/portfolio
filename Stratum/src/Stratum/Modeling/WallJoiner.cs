using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>
  /// Works out how walls meet.
  ///
  /// Two cases are resolved:
  ///
  /// **Corners.** Where exactly two wall ends share a point, both walls run past
  /// the corner and are cut back on the angle bisector, so every layer mitres
  /// cleanly and nothing is left doubled up inside the corner.
  ///
  /// **Tees.** Where a wall ends on the side of another one, the joint ties to
  /// structure: the arriving wall's core runs through the through wall's finish
  /// layers to land on its core, its own finish layers stop at the through wall's
  /// face, and the through wall's layers in between are interrupted over the width
  /// of the arriving core. That is what a rated or acoustic partition actually
  /// does, and it is the only resolution where a section cut through the junction
  /// shows something buildable.
  ///
  /// Anything more tangled - three or more walls at one point, two walls landing on
  /// the same spot from both sides - is left square. Those are detailing decisions
  /// that deserve a drawing, not a guess.
  /// </summary>
  public static class WallJoiner
  {
    /// <summary>One end of a wall, with the direction its body runs away in.
    /// Public so the tee resolution can be exercised directly by the tests.</summary>
    public class WallEnd
    {
      public WallDefinition Wall;
      public LayeredAssembly Assembly;
      public bool AtStart;
      public Point3d Point;
      /// <summary>Unit tangent pointing AWAY from the joint, along the wall.</summary>
      public Vector3d Outward;
      public double ThicknessModel;
    }

    /// <summary>Computes the junctions for every wall in the model in one pass.</summary>
    public static Dictionary<Guid, WallJunctions> Solve(RhinoDoc doc, BimModel model)
    {
      var result = new Dictionary<Guid, WallJunctions>();
      if (doc == null || model == null) return result;

      double tol = doc.ModelAbsoluteTolerance;
      double i2m = Units.InchToModel(doc);
      double snap = Math.Max(tol * 10.0, 0.5 * i2m);   // ends within half an inch count as meeting

      var ends = new List<WallEnd>();
      var flattened = new Dictionary<Guid, Curve>();

      foreach (var wall in model.Walls)
      {
        result[wall.Id] = new WallJunctions();

        var assembly = model.AssemblyOf(wall);
        if (wall.Baseline == null || assembly == null) continue;

        var flat = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);
        flattened[wall.Id] = flat;
        if (wall.Baseline.IsClosed) continue;

        double thickness = assembly.TotalThicknessIn * i2m;

        var startTangent = flat.TangentAtStart; startTangent.Z = 0; startTangent.Unitize();
        var endTangent = flat.TangentAtEnd; endTangent.Z = 0; endTangent.Unitize();

        ends.Add(new WallEnd
        {
          Wall = wall, Assembly = assembly, AtStart = true, Point = flat.PointAtStart,
          Outward = startTangent, ThicknessModel = thickness
        });
        ends.Add(new WallEnd
        {
          Wall = wall, Assembly = assembly, AtStart = false, Point = flat.PointAtEnd,
          Outward = -endTangent, ThicknessModel = thickness
        });
      }

      // ---- corners: ends that land on each other ----------------------------
      var consumed = new bool[ends.Count];
      for (int i = 0; i < ends.Count; i++)
      {
        if (consumed[i]) continue;

        var cluster = new List<int> { i };
        for (int j = i + 1; j < ends.Count; j++)
        {
          if (consumed[j]) continue;
          if (ends[i].Point.DistanceTo(ends[j].Point) <= snap) cluster.Add(j);
        }
        foreach (var k in cluster) consumed[k] = true;

        if (cluster.Count != 2) continue;                      // only clean corners mitre
        var a = ends[cluster[0]];
        var b = ends[cluster[1]];
        if (a.Wall.Id == b.Wall.Id) continue;                   // a wall closing on itself

        WallJoint ja, jb;
        if (!Miter(a, b, out ja, out jb)) continue;

        result[a.Wall.Id].Set(a.AtStart, ja);
        result[b.Wall.Id].Set(b.AtStart, jb);
      }

      // ---- tees: an unresolved end landing on the side of another wall ------
      for (int i = 0; i < ends.Count; i++)
      {
        var stem = ends[i];
        if (result[stem.Wall.Id].At(stem.AtStart).Active) continue;   // already mitred

        double station;
        var through = FindThroughWall(stem, model, flattened, snap, out station);
        if (through == null) continue;

        WallJoint joint;
        WallNotch notch;
        if (!Tee(model, stem, through, station, i2m, out joint, out notch)) continue;

        result[stem.Wall.Id].Set(stem.AtStart, joint);
        result[through.Id].Notches.Add(notch);
      }

      return result;
    }

    /// <summary>
    /// Finds a wall whose side this end lands on. Returns null when the end is
    /// free, or lands on another wall's own end (which is a corner, not a tee).
    /// </summary>
    static WallDefinition FindThroughWall(WallEnd stem, BimModel model,
                                          Dictionary<Guid, Curve> flattened,
                                          double snap, out double station)
    {
      station = 0.0;

      foreach (var candidate in model.Walls)
      {
        if (candidate.Id == stem.Wall.Id) continue;

        Curve curve;
        if (!flattened.TryGetValue(candidate.Id, out curve) || curve == null) continue;

        double t;
        if (!curve.ClosestPoint(stem.Point, out t)) continue;

        var hit = curve.PointAt(t);
        if (hit.DistanceTo(stem.Point) > snap) continue;

        // Landing on the through wall's own end is a corner, and is either already
        // mitred or deliberately left square. Not a tee.
        double lengthToHit = LengthAlong(curve, t);
        double total = curve.GetLength();
        if (lengthToHit <= snap || lengthToHit >= total - snap) continue;

        station = lengthToHit;
        return candidate;
      }

      return null;
    }

    static double LengthAlong(Curve curve, double t)
    {
      var sub = curve.Trim(curve.Domain.T0, t);
      return sub?.GetLength() ?? 0.0;
    }

    // ------------------------------------------------------------------------

    static bool Miter(WallEnd a, WallEnd b, out WallJoint ja, out WallJoint jb)
    {
      ja = WallJoint.None;
      jb = WallJoint.None;

      var ta = a.Outward; var tb = b.Outward;
      if (!ta.IsValid || !tb.IsValid) return false;

      double dot = Math.Max(-1.0, Math.Min(1.0, ta * tb));

      if (dot < -0.9995) return false;   // collinear: the walls simply run on
      if (dot > 0.9995) return false;    // doubled back: not a corner we can resolve

      var bisector = ta + tb;
      if (!bisector.Unitize()) return false;

      var normal = Vector3d.CrossProduct(Vector3d.ZAxis, bisector);
      if (!normal.Unitize()) return false;

      var corner = new Point3d(0.5 * (new Vector3d(a.Point) + new Vector3d(b.Point)));

      // Orient each plane so the wall's own body sits on the kept (negative) side.
      var na = (normal * ta) > 0 ? -normal : normal;
      var nb = (normal * tb) > 0 ? -normal : normal;

      double angle = Math.Acos(dot);
      double half = Math.Max(0.05, angle * 0.5);

      ja = MakeMiter(corner, na, Reach(a.ThicknessModel, half));
      jb = MakeMiter(corner, nb, Reach(b.ThicknessModel, half));
      return true;
    }

    static double Reach(double thickness, double halfAngle)
      => Math.Min(thickness / Math.Max(0.1, Math.Tan(halfAngle)), thickness * 20.0) + thickness;

    static WallJoint MakeMiter(Point3d origin, Vector3d normal, double extension)
    {
      var plane = new Plane(origin, normal);
      return new WallJoint
      {
        Active = true,
        Kind = JointKind.Miter,
        CorePlane = plane,
        FacePlane = plane,          // a mitre cuts every layer on the same plane
        Extension = extension
      };
    }

    /// <summary>
    /// Resolves a tee by tying to structure.
    ///
    /// The stem's core is carried through the through wall's finish layers until it
    /// lands on the through wall's core; the stem's remaining layers stop at the
    /// through wall's face; and the through wall is notched over the width of the
    /// stem's core so the two do not occupy the same space.
    /// </summary>
    /// <summary>
    /// The arithmetic of a tee, with no geometry in it.
    ///
    /// Given the two assemblies and which side the arriving wall is on, works out
    /// how far past the through wall's reference line the stem's core must run, how
    /// far its other layers run, and the band of the through wall that has to be
    /// cleared. Separated from the geometry so it can be proved by the tests rather
    /// than inspected in Rhino - it is the part that decides whether the junction is
    /// buildable.
    ///
    /// All distances are measured from the through wall's baseline, positive toward
    /// the side the stem arrives from.
    /// </summary>
    public static bool TeeDistances(LayeredAssembly throughAssembly, AssemblyJustification throughJustification,
                                    bool throughFlipped,
                                    LayeredAssembly stemAssembly, AssemblyJustification stemJustification,
                                    bool stemFlipped,
                                    double sign, double inchToModel,
                                    out double distanceToCore, out double distanceToFace,
                                    out double notchFrom, out double notchTo, out double notchWidth)
    {
      distanceToCore = distanceToFace = notchFrom = notchTo = notchWidth = 0.0;
      if (throughAssembly == null || stemAssembly == null) return false;
      if (Math.Abs(sign) < 1e-9) return false;
      sign = sign > 0 ? 1.0 : -1.0;

      var throughRanges = WallSolver.LayerRanges(throughAssembly, throughJustification,
                                                 throughFlipped, inchToModel);
      if (throughRanges.Count == 0) return false;

      int coreIndex = throughAssembly.CoreIndex;
      var throughCore = throughRanges.FirstOrDefault(r => r.Index == coreIndex);
      if (throughCore.Thickness <= 0) throughCore = throughRanges[0];

      // The face of the through wall's core that the stem lands on, and its
      // outermost face on that same side.
      double coreFace = sign > 0 ? throughCore.High : throughCore.Low;
      double outerFace = sign > 0 ? throughRanges.Max(r => r.High) : throughRanges.Min(r => r.Low);

      distanceToCore = coreFace * sign;
      distanceToFace = outerFace * sign;

      var stemRanges = WallSolver.LayerRanges(stemAssembly, stemJustification, stemFlipped, inchToModel);
      if (stemRanges.Count == 0) return false;

      var stemCore = stemRanges.FirstOrDefault(r => r.Index == stemAssembly.CoreIndex);
      if (stemCore.Thickness <= 0) stemCore = stemRanges[0];

      notchFrom = coreFace;
      notchTo = outerFace;
      notchWidth = Math.Abs(stemCore.Thickness);
      return true;
    }

    /// <summary>
    /// Resolves a tee by tying to structure.
    ///
    /// The stem's core is carried through the through wall's finish layers until it
    /// lands on the through wall's core; the stem's remaining layers stop at the
    /// through wall's face; and the through wall is notched over the width of the
    /// stem's core so the two do not occupy the same space.
    /// </summary>
    public static bool Tee(BimModel model, WallEnd stem, WallDefinition through, double station,
                           double inchToModel, out WallJoint joint, out WallNotch notch)
    {
      joint = WallJoint.None;
      notch = default(WallNotch);

      var throughAssembly = model?.AssemblyOf(through);
      if (throughAssembly == null) return false;

      var throughBaseline = WallSolver.Flatten(through.Baseline, through.BaseElevation);
      Plane frame;
      if (!WallSolver.FrameAtStation(throughBaseline, station, out frame)) return false;

      // Which side of the through wall does the stem sit on? Its body runs in the
      // outward direction from the joint.
      double side = stem.Outward * frame.YAxis;
      if (Math.Abs(side) < 1e-9) return false;                 // parallel: not a tee

      double distanceToCore, distanceToFace, notchFrom, notchTo, notchWidth;
      if (!TeeDistances(throughAssembly, through.Justification, through.Flipped,
                        stem.Assembly, stem.Wall.Justification, stem.Wall.Flipped,
                        side, inchToModel,
                        out distanceToCore, out distanceToFace,
                        out notchFrom, out notchTo, out notchWidth))
        return false;

      // Planes across the stem at the two stopping distances. The stem body runs in
      // +Outward from its end point, so the kept side is the negative side of a
      // plane whose normal points back along -Outward.
      var normal = -stem.Outward;

      joint = new WallJoint
      {
        Active = true,
        Kind = JointKind.Tee,
        CorePlane = new Plane(stem.Point + stem.Outward * distanceToCore, normal),
        FacePlane = new Plane(stem.Point + stem.Outward * distanceToFace, normal),
        // The stem may have to grow past its drawn end to reach the core.
        Extension = Math.Max(0.0, -Math.Min(distanceToCore, distanceToFace)) + stem.ThicknessModel
      };

      notch = new WallNotch
      {
        Station = station,
        Width = notchWidth,
        FromOffset = notchFrom,
        ToOffset = notchTo
      };

      return true;
    }

    /// <summary>Junctions for a single wall, for previews and one-off rebuilds.</summary>
    public static WallJunctions SolveFor(RhinoDoc doc, BimModel model, WallDefinition wall)
    {
      var all = Solve(doc, model);
      WallJunctions found;
      return all.TryGetValue(wall?.Id ?? Guid.Empty, out found) ? found : WallJunctions.None;
    }
  }
}
