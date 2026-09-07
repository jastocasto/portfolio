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

  /// <summary>Which side of the structural core a layer sits on. Side-neutral so
  /// the same model describes a wall (outer = exterior) and a floor or roof
  /// (outer = the side listed first, i.e. the top).</summary>
  public enum LayerSide
  {
    Outer = 0,
    Core = 1,
    Inner = 2
  }

  /// <summary>What kind of building element an assembly describes. Only Wall has
  /// geometry today; the others exist so the catalog, the layer stack, the opening
  /// resolutions and the schedules do not have to be rebuilt when they arrive.</summary>
  public enum AssemblyKind
  {
    Wall = 0,
    Floor = 1,
    Roof = 2,
    Ceiling = 3
  }

  /// <summary>
  /// Where the reference line sits inside the assembly.
  ///
  /// Named by layer order rather than by compass direction, so it reads correctly
  /// for a floor or a roof as well as a wall. "First" is the side whose layers are
  /// listed first - the exterior of a wall, the top of a floor. The UI and the
  /// command line translate these into the words the element actually uses; see
  /// AssemblyNaming.
  ///
  /// CoreCenter is the Stratum default: the structural core stays put and every
  /// other layer grows outward from it.
  /// </summary>
  public enum AssemblyJustification
  {
    FirstFace = 0,
    FirstCore = 1,
    CoreCenter = 2,
    LastCore = 3,
    LastFace = 4,
    Center = 5
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
