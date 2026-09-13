using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Stratum.Modeling
{
  public enum JointKind
  {
    /// <summary>Nothing to resolve; the wall ends square.</summary>
    None = 0,
    /// <summary>Two walls meet at a corner and every layer is cut on the angle
    /// bisector. Kept because an odd-angled corner sometimes wants it, but it is
    /// no longer what a corner does by default: the 45 degree line it leaves runs
    /// through every layer and prints as a joint that does not exist.</summary>
    Miter = 1,
    /// <summary>This wall dies into the side of another one.</summary>
    Tee = 2,
    /// <summary>Two walls meet at a corner and each layer is resolved against its
    /// opposite number by priority: one wall's layer runs past, the other's butts
    /// into the back of it. A corner board, not a mitre.</summary>
    Corner = 3
  }

  /// <summary>
  /// How one end of a wall terminates.
  ///
  /// A mitre cuts every layer on the same plane. A tee does not: the whole point
  /// of tying to structure is that the core runs further than the layers wrapped
  /// around it, so the two get separate planes.
  /// </summary>
  public struct WallJoint
  {
    public bool Active;
    public JointKind Kind;

    /// <summary>Where the structural core stops. The wall body is kept on the
    /// negative side of the plane.</summary>
    public Plane CorePlane;

    /// <summary>Where every non-core layer stops. Equal to <see cref="CorePlane"/>
    /// for a mitre, short of it for a tee.</summary>
    public Plane FacePlane;

    /// <summary>How far the baseline must run past its end for the cut to bite.</summary>
    public double Extension;

    public static WallJoint None => new WallJoint { Active = false, Kind = JointKind.None };

    /// <summary>
    /// A cut plane per layer index, for a joint that resolves layer by layer.
    /// Null for a mitre or a tee, which need one plane or two.
    /// </summary>
    public Dictionary<int, Plane> LayerPlanes;

    /// <summary>The plane that applies to a given layer. A per-layer plane wins
    /// where there is one; otherwise the core/face pair the tee and the mitre
    /// were built on.</summary>
    public Plane PlaneFor(int layerIndex, bool isCore)
    {
      if (LayerPlanes != null)
      {
        Plane p;
        if (LayerPlanes.TryGetValue(layerIndex, out p)) return p;
      }
      return isCore ? CorePlane : FacePlane;
    }
  }

  /// <summary>
  /// A bite taken out of a wall along its length, where another wall dies into it.
  ///
  /// This is what makes "tie to structure" true from both sides: the stem's core
  /// reaches the through wall's core only because the through wall's finish layers
  /// are interrupted over the width of that core. Without it the two walls would
  /// simply occupy the same space and the section would be a lie.
  /// </summary>
  public struct WallNotch
  {
    /// <summary>Distance along this wall's baseline to the centre of the notch,
    /// in model units.</summary>
    public double Station;

    /// <summary>Width of the notch along the wall, in model units.</summary>
    public double Width;

    /// <summary>Signed offsets across the wall that the notch clears. Any layer
    /// overlapping this band is cut over the notch's width.</summary>
    public double FromOffset, ToOffset;

    public double Low => Math.Min(FromOffset, ToOffset);
    public double High => Math.Max(FromOffset, ToOffset);

    /// <summary>True when a layer occupying [low, high] is bitten by this notch.</summary>
    public bool Touches(double low, double high, double tolerance)
      => high > Low + tolerance && low < High - tolerance;
  }

  /// <summary>Everything the builder needs to know about how one wall meets its
  /// neighbours: what happens at each end, and where other walls land on its side.</summary>
  public class WallJunctions
  {
    public WallJoint Start = WallJoint.None;
    public WallJoint End = WallJoint.None;
    public List<WallNotch> Notches = new List<WallNotch>();

    public static WallJunctions None => new WallJunctions();

    public WallJoint At(bool atStart) => atStart ? Start : End;

    public void Set(bool atStart, WallJoint joint)
    {
      if (atStart) Start = joint; else End = joint;
    }
  }
}
