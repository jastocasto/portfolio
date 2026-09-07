using System;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// A hole hosted by a wall. The opening is parametric on the host: it is
  /// positioned by arc length along the wall baseline, so moving or re-shaping
  /// the wall carries its openings with it.
  ///
  /// Every layer of the host assembly is cut individually, and each layer decides
  /// for itself how it terminates at the jamb, head and sill (see
  /// <see cref="AssemblyLayer.JambResolution"/>). That is what makes the model
  /// match what actually gets built.
  /// </summary>
  public class Opening
  {
    public Guid Id = Guid.NewGuid();
    public Guid WallId = Guid.Empty;

    /// <summary>The window or door type this is an instance of. When set, the unit
    /// decides the sizes: re-typing the unit resizes every opening using it.</summary>
    public Guid UnitId = Guid.Empty;

    public OpeningKind Kind = OpeningKind.Window;
    public string Name = "W-01";

    /// <summary>Distance in model units from the start of the wall baseline to the
    /// centre of the rough opening.</summary>
    public double StationAlongWall = 0.0;

    /// <summary>Unit (nominal) width in inches. The rough opening that is actually
    /// cut is this plus <see cref="RoughClearanceIn"/>.</summary>
    public double WidthIn = 36.0;

    /// <summary>Unit (nominal) height in inches. The rough opening that is actually
    /// cut is this plus <see cref="RoughClearanceIn"/>.</summary>
    public double HeightIn = 48.0;

    /// <summary>Height of the rough sill above the wall's base elevation, in inches.
    /// This is the standard "sill height" a window schedule calls out.
    /// Ignored for doors, which always start at the base.</summary>
    public double SillHeightIn = 36.0;

    /// <summary>Shim clearance added to the unit size to give the rough opening,
    /// in inches. 1/2" is the usual allowance for a residential window.</summary>
    public double RoughClearanceIn = 0.5;

    /// <summary>Assembly-wide override: when true the layer level resolutions in the
    /// assembly are replaced by the ones stored on this opening.</summary>
    public bool OverrideResolutions = false;
    public EdgeResolution JambOverride = EdgeResolution.Butt;
    public EdgeResolution HeadOverride = EdgeResolution.Butt;
    public EdgeResolution SillOverride = EdgeResolution.Butt;

    public string Notes = "";

    /// <summary>
    /// Copies the unit's sizes onto the opening, so the geometry code keeps reading
    /// plain numbers. Called before every build; the same pattern as
    /// WallDefinition.Resolve.
    /// </summary>
    public void Resolve(AssemblyCatalog catalog)
    {
      var unit = catalog?.FindUnit(UnitId);
      if (unit == null) return;

      Kind = unit.Kind;
      WidthIn = unit.WidthIn;
      HeightIn = unit.HeightIn;
      RoughClearanceIn = unit.RoughClearanceIn;
    }

    public double SillHeightEffectiveIn => Kind == OpeningKind.Door ? 0.0 : Math.Max(0.0, SillHeightIn);

    public double HeadHeightIn => SillHeightEffectiveIn + Math.Max(0.0, HeightIn);

    public Opening Duplicate(bool newId = true)
    {
      var o = (Opening)MemberwiseClone();
      if (newId) o.Id = Guid.NewGuid();
      return o;
    }

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "wallId", WallId);
      Ark.PutEnum(d, "kind", Kind);
      Ark.Put(d, "name", Name);
      Ark.Put(d, "station", StationAlongWall);
      Ark.Put(d, "widthIn", WidthIn);
      Ark.Put(d, "heightIn", HeightIn);
      Ark.Put(d, "sillIn", SillHeightIn);
      Ark.Put(d, "clearanceIn", RoughClearanceIn);
      Ark.Put(d, "override", OverrideResolutions);
      Ark.PutEnum(d, "jambOv", JambOverride);
      Ark.PutEnum(d, "headOv", HeadOverride);
      Ark.PutEnum(d, "sillOv", SillOverride);
      Ark.Put(d, "notes", Notes);
      return d;
    }

    public static Opening FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      return new Opening
      {
        Id = Ark.Id(d, "id"),
        WallId = Ark.Id(d, "wallId"),
        Kind = Ark.Enum(d, "kind", OpeningKind.Window),
        Name = Ark.Str(d, "name", "Opening"),
        StationAlongWall = Ark.Num(d, "station"),
        WidthIn = Ark.Num(d, "widthIn", 36.0),
        HeightIn = Ark.Num(d, "heightIn", 48.0),
        SillHeightIn = Ark.Num(d, "sillIn", 36.0),
        RoughClearanceIn = Ark.Num(d, "clearanceIn", 0.5),
        OverrideResolutions = Ark.Bool(d, "override"),
        JambOverride = Ark.Enum(d, "jambOv", EdgeResolution.Butt),
        HeadOverride = Ark.Enum(d, "headOv", EdgeResolution.Butt),
        SillOverride = Ark.Enum(d, "sillOv", EdgeResolution.Butt),
        Notes = Ark.Str(d, "notes")
      };
    }
  }
}
