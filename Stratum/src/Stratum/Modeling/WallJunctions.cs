using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Stratum.Modeling
{
  public enum JointKind
  {
    /// <summary>Nothing to resolve; the wall ends square.</summary>
    None = 0,
    /// <summary>Two walls meet at a corner and are cut back on the angle bisector.</summary>
    Miter = 1,
    /// <summary>This wall dies into the side of another one.</summary>
    Tee = 2
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

    /// <summary>The plane that applies to a given layer.</summary>
    public Plane PlaneFor(bool isCore) => isCore ? CorePlane : FacePlane;
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
