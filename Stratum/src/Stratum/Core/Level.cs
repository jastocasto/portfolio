using System;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// A building level: a named elevation that things are built from.
  ///
  /// Walls attach to a level with an offset rather than carrying a raw elevation,
  /// so changing a floor-to-floor moves everything that sits on it instead of
  /// requiring every wall to be re-entered. Window sill and head heights are read
  /// against the level too, which is how they are called out on a schedule.
  /// </summary>
  public class Level
  {
    public Guid Id = Guid.NewGuid();

    public string Name = "Level 1";

    /// <summary>Elevation in model units, measured on world Z.</summary>
    public double Elevation = 0.0;

    /// <summary>Orders levels in the UI when two share an elevation.</summary>
    public int SortOrder = 0;

    /// <summary>False for a reference datum that nothing is built from, such as
    /// a top-of-footing or a grade line.</summary>
    public bool IsStorey = true;

    public Level Duplicate(bool newId = true)
    {
      var level = (Level)MemberwiseClone();
      if (newId) level.Id = Guid.NewGuid();
      return level;
    }

    public override string ToString() => Name;

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "name", Name);
      Ark.Put(d, "elevation", Elevation);
      Ark.Put(d, "sortOrder", SortOrder);
      Ark.Put(d, "isStorey", IsStorey);
      return d;
    }

    public static Level FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      return new Level
      {
        Id = Ark.Id(d, "id"),
        Name = Ark.Str(d, "name", "Level"),
        Elevation = Ark.Num(d, "elevation"),
        SortOrder = Ark.Int(d, "sortOrder"),
        IsStorey = Ark.Bool(d, "isStorey", true)
      };
    }
  }

  /// <summary>How the top of a wall is decided.</summary>
  public enum WallTopMode
  {
    /// <summary>An explicit height above the wall's base.</summary>
    Height = 0,

    /// <summary>Up to another level, plus an offset. A floor-to-floor change flows
    /// through to every wall bound this way.</summary>
    ToLevel = 1,

    /// <summary>
    /// Up to a surface or polysurface in the document, cut at that surface's own
    /// angle. This is the gable case: the wall rises to meet the underside of the
    /// roof rather than stopping flat, and every layer rakes with it.
    /// </summary>
    ToSurface = 2
  }
}
