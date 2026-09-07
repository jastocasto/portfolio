using System;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// A window or door type: the thing you order, schedule and price.
  ///
  /// Openings reference a unit rather than carrying loose sizes, so re-typing one
  /// window resizes every instance of it and the schedule counts them properly.
  ///
  /// Stratum deliberately does NOT model the frame, sash and glazing. Instead a unit
  /// can name a Rhino block: if a block definition with that name exists in the
  /// document, every opening of that type gets an instance of it, positioned and
  /// oriented in the rough opening. That way your own window and door geometry drops
  /// straight in, and the plug-in stays responsible for the hole, the layer
  /// terminations and the schedule.
  ///
  /// Block convention: draw the unit in the world XY plane as if looking at it from
  /// OUTSIDE. X is width, centred on the origin. Y is height, with the origin at the
  /// rough sill. Z is depth back through the wall, positive toward the interior.
  /// Stratum places the origin on the exterior face of the wall, shifted in by
  /// <see cref="BlockInsetIn"/>.
  /// </summary>
  public class OpeningUnit
  {
    public Guid Id = Guid.NewGuid();

    public OpeningKind Kind = OpeningKind.Window;

    /// <summary>Letter the mark is built from: W-01, D-03.</summary>
    public string MarkPrefix = "W";

    public string Name = "New window";
    public string Manufacturer = "";
    public string Model = "";
    public string Sku = "";

    /// <summary>Unit (nominal) size in inches. The rough opening is this plus
    /// <see cref="RoughClearanceIn"/>.</summary>
    public double WidthIn = 36.0;
    public double HeightIn = 48.0;

    /// <summary>Shim clearance added to the unit size to give the rough opening.</summary>
    public double RoughClearanceIn = 0.5;

    /// <summary>How it operates: Fixed, Casement, Double-hung, Slider, Awning,
    /// or for a door Swing Left / Swing Right / Sliding / Bi-fold.</summary>
    public string Operation = "Fixed";

    public string Glazing = "";

    // ---- performance, for the schedule and the energy model ------------------

    /// <summary>Assembly U-factor, Btu/h·ft²·°F. The inverse of R for a window.</summary>
    public double UFactor = 0.30;

    /// <summary>Solar heat gain coefficient, 0-1.</summary>
    public double SHGC = 0.30;

    /// <summary>Visible transmittance, 0-1.</summary>
    public double VisibleTransmittance = 0.55;

    /// <summary>Air leakage, cfm/ft². Below 0.30 is typical for a rated unit.</summary>
    public double AirLeakage = 0.20;

    public double Cost = 0.0;
    public string HardwareNotes = "";
    public string Notes = "";

    // ---- your geometry -------------------------------------------------------

    /// <summary>Name of a Rhino block to place in every opening of this type.
    /// Empty means no geometry: the hole is still cut and scheduled.</summary>
    public string BlockName = "";

    /// <summary>How far the block origin sits in from the exterior face, in inches.
    /// Use it to set the unit back in the opening without redrawing the block.</summary>
    public double BlockInsetIn = 0.0;

    public double RoughWidthIn => Math.Max(0.0, WidthIn + RoughClearanceIn);
    public double RoughHeightIn => Math.Max(0.0, HeightIn + RoughClearanceIn);

    /// <summary>R-value equivalent, for putting a window alongside a wall assembly.</summary>
    public double RValue => UFactor > 1e-9 ? 1.0 / UFactor : 0.0;

    public string DisplayName
    {
      get
      {
        string size = Units.FeetInchesShort(WidthIn) + " x " + Units.FeetInchesShort(HeightIn);
        return string.IsNullOrWhiteSpace(Name) ? size : Name + "  (" + size + ")";
      }
    }

    public OpeningUnit Duplicate(bool newId = true)
    {
      var unit = (OpeningUnit)MemberwiseClone();
      if (newId) unit.Id = Guid.NewGuid();
      return unit;
    }

    public override string ToString() => DisplayName;

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.PutEnum(d, "kind", Kind);
      Ark.Put(d, "markPrefix", MarkPrefix);
      Ark.Put(d, "name", Name);
      Ark.Put(d, "manufacturer", Manufacturer);
      Ark.Put(d, "model", Model);
      Ark.Put(d, "sku", Sku);
      Ark.Put(d, "widthIn", WidthIn);
      Ark.Put(d, "heightIn", HeightIn);
      Ark.Put(d, "clearanceIn", RoughClearanceIn);
      Ark.Put(d, "operation", Operation);
      Ark.Put(d, "glazing", Glazing);
      Ark.Put(d, "uFactor", UFactor);
      Ark.Put(d, "shgc", SHGC);
      Ark.Put(d, "vt", VisibleTransmittance);
      Ark.Put(d, "airLeakage", AirLeakage);
      Ark.Put(d, "cost", Cost);
      Ark.Put(d, "hardware", HardwareNotes);
      Ark.Put(d, "notes", Notes);
      Ark.Put(d, "blockName", BlockName);
      Ark.Put(d, "blockInset", BlockInsetIn);
      return d;
    }

    public static OpeningUnit FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      return new OpeningUnit
      {
        Id = Ark.Id(d, "id"),
        Kind = Ark.Enum(d, "kind", OpeningKind.Window),
        MarkPrefix = Ark.Str(d, "markPrefix", "W"),
        Name = Ark.Str(d, "name", "Unit"),
        Manufacturer = Ark.Str(d, "manufacturer"),
        Model = Ark.Str(d, "model"),
        Sku = Ark.Str(d, "sku"),
        WidthIn = Ark.Num(d, "widthIn", 36.0),
        HeightIn = Ark.Num(d, "heightIn", 48.0),
        RoughClearanceIn = Ark.Num(d, "clearanceIn", 0.5),
        Operation = Ark.Str(d, "operation", "Fixed"),
        Glazing = Ark.Str(d, "glazing"),
        UFactor = Ark.Num(d, "uFactor", 0.30),
        SHGC = Ark.Num(d, "shgc", 0.30),
        VisibleTransmittance = Ark.Num(d, "vt", 0.55),
        AirLeakage = Ark.Num(d, "airLeakage", 0.20),
        Cost = Ark.Num(d, "cost"),
        HardwareNotes = Ark.Str(d, "hardware"),
        Notes = Ark.Str(d, "notes"),
        BlockName = Ark.Str(d, "blockName"),
        BlockInsetIn = Ark.Num(d, "blockInset")
      };
    }
  }
}
