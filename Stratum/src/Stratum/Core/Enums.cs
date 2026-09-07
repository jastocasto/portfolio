namespace Stratum.Core
{
  /// <summary>What a layer does in the assembly. Drives sorting, schedules,
  /// code checks and the default way a layer resolves at an opening.</summary>
  public enum LayerFunction
  {
    Finish = 0,        // gypsum board, plaster, tile
    Furring = 1,       // strapping, service cavity
    Membrane = 2,      // WRB, vapour retarder, air barrier (thin)
    Insulation = 3,    // batt, board, cavity fill
    AirGap = 4,        // ventilated / drained cavity
    Sheathing = 5,     // plywood, OSB, gypsum sheathing
    Structure = 6,     // studs, CMU, concrete - the core
    Cladding = 7       // siding, brick veneer, panel
  }

  /// <summary>Which side of the structural core a layer sits on.</summary>
  public enum LayerSide
  {
    Exterior = 0,
    Core = 1,
    Interior = 2
  }

  /// <summary>Where the drawn baseline sits inside the assembly.
  /// CoreCenter is the Stratum default: the structural core stays put and
  /// every other layer grows outward from it.</summary>
  public enum WallJustification
  {
    ExteriorFace = 0,
    ExteriorCore = 1,
    CoreCenter = 2,
    InteriorCore = 3,
    InteriorFace = 4,
    WallCenter = 5
  }

  /// <summary>How a single layer terminates at the edge of an opening.</summary>
  public enum EdgeResolution
  {
    /// <summary>Layer stops flush with the rough opening.</summary>
    Butt = 0,
    /// <summary>Layer turns into the reveal and lines the jamb / head / sill.</summary>
    Wrap = 1,
    /// <summary>Layer runs in only as far as the frame it dies into.</summary>
    ReturnToFrame = 2,
    /// <summary>Layer is held back from the rough opening (sealant joint, drainage).</summary>
    HoldBack = 3,
    /// <summary>Layer is not cut at all (continuous air barrier taped across, etc.).</summary>
    Continuous = 4
  }

  public enum OpeningKind
  {
    Window = 0,
    Door = 1,
    Opening = 2
  }
}
