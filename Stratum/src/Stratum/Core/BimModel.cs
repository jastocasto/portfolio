using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Assembly used by the next wall the user draws.</summary>
    public Guid ActiveAssemblyId = Guid.Empty;
    public WallJustification ActiveJustification = WallJustification.CoreCenter;

    /// <summary>Default wall height in model units. Zero means "not set yet";
    /// commands substitute 8 feet in the document's units.</summary>
    public double ActiveHeight = 0.0;

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

    public WallAssembly AssemblyOf(WallDefinition wall)
      => wall == null ? null : Catalog.FindAssembly(wall.AssemblyId);

    public WallAssembly ActiveAssembly
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
      return m;
    }

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "schema", SchemaVersion);
      Ark.Put(d, "catalog", Catalog.ToDictionary());
      Ark.PutList(d, "walls", Walls.Select(w => w.ToDictionary()).ToList());
      Ark.Put(d, "activeAssembly", ActiveAssemblyId);
      Ark.PutEnum(d, "activeJustification", ActiveJustification);
      Ark.Put(d, "activeHeight", ActiveHeight);
      return d;
    }

    public static BimModel FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return CreateDefault();

      var m = new BimModel
      {
        Catalog = AssemblyCatalog.FromDictionary(Ark.Dict(d, "catalog")),
        ActiveAssemblyId = Ark.Id(d, "activeAssembly"),
        ActiveJustification = Ark.Enum(d, "activeJustification", WallJustification.CoreCenter),
        ActiveHeight = Ark.Num(d, "activeHeight")
      };

      foreach (var wd in Ark.List(d, "walls"))
      {
        var w = WallDefinition.FromDictionary(wd);
        if (w != null) m.Walls.Add(w);
      }

      // A document written by an older build, or one whose catalog was cleared,
      // still needs something to draw with.
      if (m.Catalog.Assemblies.Count == 0)
        m.Catalog.Merge(CatalogDefaults.Create());

      if (m.Catalog.FindAssembly(m.ActiveAssemblyId) == null)
        m.ActiveAssemblyId = m.Catalog.Assemblies.FirstOrDefault()?.Id ?? Guid.Empty;

      return m;
    }
  }
}
