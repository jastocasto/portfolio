using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// Everything Stratum knows about one Rhino document: the catalog it was drawn
  /// with, and the parametric definition of every wall in it.
  /// </summary>
  public class BimModel
  {
    public const int SchemaVersion = 1;

    public AssemblyCatalog Catalog = new AssemblyCatalog();
    public List<WallDefinition> Walls = new List<WallDefinition>();

    /// <summary>Building levels, kept sorted by elevation.</summary>
    public List<Level> Levels = new List<Level>();

    public List<SlabDefinition> Slabs = new List<SlabDefinition>();
    public List<RoofDefinition> Roofs = new List<RoofDefinition>();

    /// <summary>Assembly used by the next wall the user draws.</summary>
    public Guid ActiveAssemblyId = Guid.Empty;
    public AssemblyJustification ActiveJustification = AssemblyJustification.CoreCenter;

    /// <summary>Default wall height in model units. Zero means "not set yet";
    /// commands substitute 8 feet in the document's units.</summary>
    public double ActiveHeight = 0.0;

    public SlabDefinition FindSlab(Guid id)
      => id == Guid.Empty ? null : Slabs.FirstOrDefault(s => s.Id == id);

    public RoofDefinition FindRoof(Guid id)
      => id == Guid.Empty ? null : Roofs.FirstOrDefault(r => r.Id == id);

    /// <summary>Every layered element that references an assembly, whatever its kind.
    /// Used when an assembly edit has to reach everything built from it.</summary>
    public IEnumerable<LayeredElement> ElementsUsing(Guid assemblyId)
      => Slabs.Cast<LayeredElement>().Concat(Roofs)
              .Where(e => e.AssemblyId == assemblyId);

    /// <summary>
    /// Corners where the wall that runs past has been swapped by hand.
    ///
    /// A corner board has a front and a back: one wall's cladding turns the
    /// corner and the other butts into it. Which way round is a drawing
    /// decision - it decides which elevation the joint reads on - and draw order
    /// picks it by default, which is stable but arbitrary. A key appears here
    /// only where that default has been overridden.
    /// </summary>
    public HashSet<string> CornerFlips = new HashSet<string>();

    /// <summary>Order-independent key for the two walls at a corner, so it does
    /// not matter which one the solver happens to call first.</summary>
    public static string CornerKey(Guid a, Guid b)
      => a.CompareTo(b) <= 0 ? a.ToString() + "|" + b.ToString()
                             : b.ToString() + "|" + a.ToString();

    public bool IsCornerFlipped(Guid a, Guid b) => CornerFlips.Contains(CornerKey(a, b));

    /// <summary>Swaps which wall runs past at a corner. Returns the new state:
    /// true when the corner is now flipped away from its default.</summary>
    public bool ToggleCornerFlip(Guid a, Guid b)
    {
      var key = CornerKey(a, b);
      if (CornerFlips.Remove(key)) return false;
      CornerFlips.Add(key);
      return true;
    }

    public Level FindLevel(Guid id)
      => id == Guid.Empty ? null : Levels.FirstOrDefault(l => l.Id == id);

    public IEnumerable<Level> SortedLevels
      => Levels.OrderBy(l => l.Elevation).ThenBy(l => l.SortOrder);

    /// <summary>The level immediately above the given one, or null at the top.</summary>
    public Level LevelAbove(Level level)
      => level == null ? null
       : SortedLevels.FirstOrDefault(l => l.Elevation > level.Elevation + RhinoMath.ZeroTolerance);

    /// <summary>The level a raw elevation most likely belongs to: the highest one
    /// at or below it. Used to adopt walls from files written before levels existed.</summary>
    public Level LevelFor(double elevation)
    {
      Level best = null;
      foreach (var level in SortedLevels)
        if (level.Elevation <= elevation + RhinoMath.ZeroTolerance) best = level;
      return best ?? SortedLevels.FirstOrDefault();
    }

    /// <summary>Guarantees at least one level, and binds any wall that has none.
    /// Called when a document is prepared, so older files gain levels quietly.</summary>
    public void EnsureLevels()
    {
      if (Levels.Count == 0)
        Levels.Add(new Level { Name = "Level 1", Elevation = 0.0, SortOrder = 0 });

      foreach (var wall in Walls)
      {
        if (FindLevel(wall.LevelId) != null) continue;

        var level = LevelFor(wall.BaseElevation);
        if (level == null) continue;

        wall.LevelId = level.Id;
        wall.BaseOffset = wall.BaseElevation - level.Elevation;
      }
    }

    public WallDefinition FindWall(Guid id)
      => id == Guid.Empty ? null : Walls.FirstOrDefault(w => w.Id == id);

    public WallDefinition FindWallByObjectId(Guid objectId)
      => objectId == Guid.Empty ? null : Walls.FirstOrDefault(w => w.LayerObjectIds.Contains(objectId));

    public Opening FindOpening(Guid id)
    {
      foreach (var w in Walls)
      {
        var o = w.Openings.FirstOrDefault(x => x.Id == id);
        if (o != null) return o;
      }
      return null;
    }

    public IEnumerable<WallDefinition> WallsUsing(Guid assemblyId)
      => Walls.Where(w => w.AssemblyId == assemblyId);

    public LayeredAssembly AssemblyOf(WallDefinition wall)
      => wall == null ? null : Catalog.FindAssembly(wall.AssemblyId);

    public LayeredAssembly ActiveAssembly
    {
      get
      {
        var a = Catalog.FindAssembly(ActiveAssemblyId);
        if (a == null) a = Catalog.Assemblies.FirstOrDefault();
        return a;
      }
    }

    public static BimModel CreateDefault()
    {
      var m = new BimModel { Catalog = CatalogDefaults.Create() };
      m.ActiveAssemblyId = m.Catalog.Assemblies.FirstOrDefault()?.Id ?? Guid.Empty;
      m.EnsureLevels();
      return m;
    }

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "schema", SchemaVersion);
      Ark.Put(d, "catalog", Catalog.ToDictionary());
      Ark.PutList(d, "levels", Levels.Select(l => l.ToDictionary()).ToList());
      Ark.PutList(d, "slabs", Slabs.Select(x => x.ToDictionary()).ToList());
      Ark.PutList(d, "roofs", Roofs.Select(x => x.ToDictionary()).ToList());
      Ark.PutList(d, "walls", Walls.Select(w => w.ToDictionary()).ToList());
      Ark.Put(d, "activeAssembly", ActiveAssemblyId);
      Ark.PutEnum(d, "activeJustification", ActiveJustification);
      Ark.Put(d, "activeHeight", ActiveHeight);
      Ark.Put(d, "cornerFlips", string.Join(";", CornerFlips));
      return d;
    }

    public static BimModel FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return CreateDefault();

      var m = new BimModel
      {
        Catalog = AssemblyCatalog.FromDictionary(Ark.Dict(d, "catalog")),
        ActiveAssemblyId = Ark.Id(d, "activeAssembly"),
        ActiveJustification = Ark.Enum(d, "activeJustification", AssemblyJustification.CoreCenter),
        ActiveHeight = Ark.Num(d, "activeHeight")
      };

      foreach (var ld in Ark.List(d, "levels"))
      {
        var level = Level.FromDictionary(ld);
        if (level != null) m.Levels.Add(level);
      }

      foreach (var sd in Ark.List(d, "slabs"))
      {
        var slab = SlabDefinition.FromDictionary(sd);
        if (slab != null) m.Slabs.Add(slab);
      }

      foreach (var rd in Ark.List(d, "roofs"))
      {
        var roof = RoofDefinition.FromDictionary(rd);
        if (roof != null) m.Roofs.Add(roof);
      }

      foreach (var wd in Ark.List(d, "walls"))
      {
        var w = WallDefinition.FromDictionary(wd);
        if (w != null) m.Walls.Add(w);
      }

      // Absent in documents written before corners could be flipped, which is
      // right: no key means every corner takes its default.
      foreach (var key in Ark.Str(d, "cornerFlips")
                             .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        m.CornerFlips.Add(key);

      // A document written by an older build, or one whose catalog was cleared,
      // still needs something to draw with.
      if (m.Catalog.Assemblies.Count == 0)
        m.Catalog.Merge(CatalogDefaults.Create());

      if (m.Catalog.FindAssembly(m.ActiveAssemblyId) == null)
        m.ActiveAssemblyId = m.Catalog.Assemblies.FirstOrDefault()?.Id ?? Guid.Empty;

      m.EnsureLevels();
      return m;
    }
  }
}
