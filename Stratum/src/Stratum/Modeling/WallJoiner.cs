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

      foreach (var wall in model.Walls) result[wall.Id] = new WallJunctions();

      Dictionary<Guid, Curve> flattened;
      var ends = Ends(model, i2m, out flattened);

      // ---- corners: ends that land on each other ----------------------------
      foreach (var pair in CornerClusters(ends, snap))
      {
        var a = ends[pair[0]];
        var b = ends[pair[1]];

        WallJoint ja, jb;
        if (!Corner(model, a, b, i2m, tol, out ja, out jb)) continue;

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

    /// <summary>Both ends of every wall that can take a joint, with the
    /// direction each one's body runs away in. Shared by the solver and by
    /// anything that has to name a junction - a flip command, a report.</summary>
    static List<WallEnd> Ends(BimModel model, double inchToModel,
                              out Dictionary<Guid, Curve> flattened)
    {
      var ends = new List<WallEnd>();
      flattened = new Dictionary<Guid, Curve>();
      if (model == null) return ends;

      foreach (var wall in model.Walls)
      {
        var assembly = model.AssemblyOf(wall);
        if (wall?.Baseline == null || assembly == null) continue;

        var flat = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);
        if (flat == null) continue;
        flattened[wall.Id] = flat;
        if (wall.Baseline.IsClosed) continue;

        double thickness = assembly.TotalThicknessIn * inchToModel;

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

      return ends;
    }

    /// <summary>Pairs of end indices that land on the same point. Exactly two:
    /// three or more ends meeting is a detailing decision that deserves a
    /// drawing, not a guess, and is left square.</summary>
    static List<int[]> CornerClusters(List<WallEnd> ends, double snap)
    {
      var pairs = new List<int[]>();
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

        if (cluster.Count != 2) continue;
        if (ends[cluster[0]].Wall.Id == ends[cluster[1]].Wall.Id) continue;  // a wall closing on itself
        pairs.Add(new[] { cluster[0], cluster[1] });
      }

      return pairs;
    }

    /// <summary>A corner in the model: the two walls, where they meet, and which
    /// one's layers currently run past.</summary>
    public class CornerPair
    {
      public WallDefinition A;
      public WallDefinition B;
      public Point3d Point;
      public WallDefinition Winner;
      public WallDefinition Loser => ReferenceEquals(Winner, A) ? B : A;
      public bool Flipped;
    }

    /// <summary>Every corner in the model, for anything that has to name one.</summary>
    public static List<CornerPair> Corners(RhinoDoc doc, BimModel model)
    {
      var found = new List<CornerPair>();
      if (doc == null || model == null) return found;

      double tol = doc.ModelAbsoluteTolerance;
      double i2m = Units.InchToModel(doc);
      double snap = Math.Max(tol * 10.0, 0.5 * i2m);

      Dictionary<Guid, Curve> flattened;
      var ends = Ends(model, i2m, out flattened);

      foreach (var pair in CornerClusters(ends, snap))
      {
        var a = ends[pair[0]];
        var b = ends[pair[1]];
        found.Add(new CornerPair
        {
          A = a.Wall,
          B = b.Wall,
          Point = new Point3d(0.5 * (new Vector3d(a.Point) + new Vector3d(b.Point))),
          Winner = WinnerOf(model, a.Wall, b.Wall),
          Flipped = model.IsCornerFlipped(a.Wall.Id, b.Wall.Id)
        });
      }

      return found;
    }

    /// <summary>
    /// Which wall's layers run past at a corner.
    ///
    /// The earlier wall in the model by default - stable, and arbitrary in the
    /// way Revit's join order is arbitrary - unless the corner has been flipped
    /// by hand, which is a drawing decision and belongs to the user.
    /// </summary>
    public static WallDefinition WinnerOf(BimModel model, WallDefinition a, WallDefinition b)
    {
      if (model == null || a == null || b == null) return a;
      bool aWins = model.Walls.IndexOf(a) <= model.Walls.IndexOf(b);
      if (model.IsCornerFlipped(a.Id, b.Id)) aWins = !aWins;
      return aWins ? a : b;
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

    /// <summary>
    /// A corner, resolved layer by layer instead of on the angle bisector.
    ///
    /// One wall's layers run past the corner; the other wall's matching layers
    /// butt into the back of them. A corner board, which is what gets built, and
    /// not a mitre, which does not - and whose 45 degree line, being real
    /// geometry, prints in every plan and section as a joint that is not there.
    ///
    /// Falls back to the mitre for anything this cannot resolve, so a corner is
    /// never simply left doubled up.
    /// </summary>
    static bool Corner(BimModel model, WallEnd a, WallEnd b, double inchToModel,
                       double tolerance, out WallJoint ja, out WallJoint jb)
    {
      // The mitre planes are the fallback for any layer the per-layer pass does
      // not place, and they carry the reach and the validity checks already.
      if (!Miter(a, b, out ja, out jb)) return false;
      if (model == null || a.Assembly == null || b.Assembly == null) return true;

      Plane fa, fb;
      if (!FrameAtJoint(a, out fa) || !FrameAtJoint(b, out fb)) return true;

      var ra = WallSolver.LayerRanges(a.Assembly, a.Wall.Justification, a.Wall.Flipped, inchToModel);
      var rb = WallSolver.LayerRanges(b.Assembly, b.Wall.Justification, b.Wall.Flipped, inchToModel);
      if (ra.Count == 0 || rb.Count == 0) return true;

      // Somebody has to win. Draw order decides unless the corner has been
      // flipped by hand - see BimCornerFlip.
      bool aWins = ReferenceEquals(WinnerOf(model, a.Wall, b.Wall), a.Wall);

      var pa = Resolve(a, fa, ra, b, fb, rb, aWins, tolerance);
      var pb = Resolve(b, fb, rb, a, fa, ra, !aWins, tolerance);
      if (pa == null && pb == null) return true;      // nothing placed - keep the mitre

      if (pa != null) { ja.Kind = JointKind.Corner; ja.LayerPlanes = pa; }
      if (pb != null) { jb.Kind = JointKind.Corner; jb.LayerPlanes = pb; }

      // A layer that runs past has to reach the far side of the other wall, which
      // is further than a mitre ever cuts.
      ja.Extension = Math.Max(ja.Extension, b.ThicknessModel * 2.0 + a.ThicknessModel);
      jb.Extension = Math.Max(jb.Extension, a.ThicknessModel * 2.0 + b.ThicknessModel);
      return true;
    }

    /// <summary>
    /// Where every layer of one wall stops at a corner.
    ///
    /// A layer travels toward the corner and meets the other wall's stack side
    /// on. It is halted by the first band it may not pass through: the first of
    /// the other wall's layers whose offsets overlap its own and whose priority
    /// is equal or stronger. Winning means running past that band to its far
    /// face; losing means butting into its near face.
    ///
    /// Two identical assemblies therefore give a corner post where the studs
    /// meet, siding that wraps with the other wall's siding butting behind it,
    /// and gypsum that wraps at the inside corner - from one rule, with no layer
    /// named anywhere and no special cases.
    /// </summary>
    static Dictionary<int, Plane> Resolve(WallEnd x, Plane fx, List<LayerRange> rx,
                                          WallEnd y, Plane fy, List<LayerRange> ry,
                                          bool xWins, double tolerance)
    {
      var tx = x.Outward;
      var ny = fy.YAxis;

      // Offset in Y's frame per unit travelled along X. Zero means the walls are
      // parallel, which is not a corner this can resolve.
      double denom = tx * ny;
      if (Math.Abs(denom) < 1e-9) return null;

      double tol = Math.Max(tolerance, 1e-9);
      var planes = new Dictionary<int, Plane>();

      foreach (var range in rx)
      {
        int priority = PriorityOf(x.Assembly, range.Index);

        // X's body runs in +Outward, so travelling toward the corner is the
        // station decreasing. The first band met is the one with the largest
        // near station.
        LayerRange? stop = null;
        double nearest = double.NegativeInfinity;

        foreach (var other in ry)
        {
          if (other.High <= range.Low + tol) continue;        // no overlap across the wall
          if (other.Low >= range.High - tol) continue;
          if (PriorityOf(y.Assembly, other.Index) > priority) continue;   // weaker: pass through

          double s0 = other.Low / denom, s1 = other.High / denom;
          double near = Math.Max(s0, s1);
          if (near > nearest) { nearest = near; stop = other; }
        }

        if (stop == null) continue;      // nothing stops it; the mitre plane still applies

        double t0 = stop.Value.Low / denom, t1 = stop.Value.High / denom;
        double station = xWins ? Math.Min(t0, t1) : Math.Max(t0, t1);

        // Normal points back along the wall so the body sits on the kept
        // (negative) side, which is what Brep.Trim keeps.
        var plane = new Plane(x.Point + tx * station, -tx);
        if (plane.IsValid) planes[range.Index] = plane;
      }

      return planes.Count > 0 ? planes : null;
    }

    /// <summary>The layer frame at the end of a wall that meets a joint. Its
    /// YAxis is the direction layer offsets are measured along, the same one
    /// WallSolver.LayerRanges reports them in.</summary>
    static bool FrameAtJoint(WallEnd end, out Plane frame)
    {
      frame = Plane.Unset;
      if (end?.Wall?.Baseline == null) return false;

      var flat = WallSolver.Flatten(end.Wall.Baseline, end.Wall.BaseElevation);
      if (flat == null) return false;

      double station = end.AtStart ? 0.0 : flat.GetLength();
      return WallSolver.FrameAtStation(flat, station, out frame);
    }

    static int PriorityOf(LayeredAssembly assembly, int index)
    {
      if (assembly == null || index < 0 || index >= assembly.Layers.Count)
        return AssemblyLayer.DefaultPriority(LayerFunction.Finish);
      return assembly.Layers[index].Priority;
    }

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

    /// <summary>
    /// The given walls plus every wall that shares a junction with one of them.
    ///
    /// A junction is solved from BOTH walls' layer stacks - a tee stops its stem
    /// on the through wall's core and its finishes on that wall's face, and the
    /// through wall is notched by the stem's core. So editing one wall type
    /// invalidates the geometry of its neighbours as well as of the walls that
    /// use it. Rebuilding only WallsUsing(assembly) leaves a partition tee'd into
    /// a wall whose thickness just changed sitting on its old stopping plane,
    /// either floating clear of it or buried in it.
    ///
    /// Deliberately generous: this tests whether the baselines come near each
    /// other, not whether a junction actually resolved. Rebuilding a wall that
    /// did not need it costs a moment. Missing one leaves wrong geometry in the
    /// document, and nothing says so.
    /// </summary>
    public static List<WallDefinition> Touching(RhinoDoc doc, BimModel model,
                                                IEnumerable<WallDefinition> walls)
    {
      var seed = (walls ?? Enumerable.Empty<WallDefinition>())
                 .Where(w => w != null).ToList();
      if (doc == null || model == null || seed.Count == 0) return seed;

      double snap = Math.Max(doc.ModelAbsoluteTolerance * 10.0, 0.5 * Units.InchToModel(doc));

      var flat = new Dictionary<Guid, Curve>();
      foreach (var w in model.Walls)
      {
        if (w?.Baseline == null) continue;
        var c = WallSolver.Flatten(w.Baseline, w.BaseElevation);
        if (c != null) flat[w.Id] = c;
      }

      var result = new List<WallDefinition>(seed);
      var have = new HashSet<Guid>(seed.Select(w => w.Id));

      foreach (var candidate in model.Walls)
      {
        if (candidate == null || have.Contains(candidate.Id)) continue;

        Curve cc;
        if (!flat.TryGetValue(candidate.Id, out cc)) continue;

        foreach (var w in seed)
        {
          Curve wc;
          if (!flat.TryGetValue(w.Id, out wc)) continue;
          if (!NearlyMeet(wc, cc, snap)) continue;

          result.Add(candidate);
          have.Add(candidate.Id);
          break;
        }
      }

      return result;
    }

    /// <summary>True when two baselines cross, or either one's end lands on the
    /// other - a corner and a tee both look like this.</summary>
    static bool NearlyMeet(Curve a, Curve b, double snap)
    {
      var hits = Rhino.Geometry.Intersect.Intersection.CurveCurve(a, b, snap, snap);
      if (hits != null && hits.Count > 0) return true;

      return EndLandsOn(a, b, snap) || EndLandsOn(b, a, snap);
    }

    static bool EndLandsOn(Curve ends, Curve host, double snap)
    {
      foreach (var p in new[] { ends.PointAtStart, ends.PointAtEnd })
      {
        double t;
        if (!host.ClosestPoint(p, out t)) continue;
        if (host.PointAt(t).DistanceTo(p) <= snap) return true;
      }
      return false;
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
