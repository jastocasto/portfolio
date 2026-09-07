using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Collections;
using Rhino.Geometry;

namespace Stratum.Core
{
  /// <summary>
  /// The parametric definition of one wall. The Rhino objects in the document are
  /// *output*: they are regenerated from this record whenever anything changes.
  /// </summary>
  public class WallDefinition
  {
    public Guid Id = Guid.NewGuid();
    public Guid AssemblyId = Guid.Empty;

    public string Name = "";

    /// <summary>Wall reference line, in model units and model space.</summary>
    public Curve Baseline;

    /// <summary>Bottom of wall, model units, measured on world Z.</summary>
    public double BaseElevation = 0.0;

    /// <summary>Wall height, model units.</summary>
    public double Height = 8.0;

    public AssemblyJustification Justification = AssemblyJustification.CoreCenter;

    /// <summary>Swaps which side of the baseline is the exterior.</summary>
    public bool Flipped = false;

    public List<Opening> Openings = new List<Opening>();

    // ---- document links (rebuilt, not authored) ----------------------------

    /// <summary>Rhino object ids of the layer solids, in assembly order.</summary>
    public List<Guid> LayerObjectIds = new List<Guid>();

    /// <summary>Index of the Rhino group that keeps the layer solids acting as
    /// one wall. -1 when the wall has not been baked yet.</summary>
    public int GroupIndex = -1;

    public string GroupName => "Wall " + Id.ToString("N").Substring(0, 8);

    public double TopElevation => BaseElevation + Height;

    public double Length => Baseline?.GetLength() ?? 0.0;

    public WallDefinition Duplicate(bool newId = true)
    {
      var w = new WallDefinition
      {
        Id = newId ? Guid.NewGuid() : Id,
        AssemblyId = AssemblyId,
        Name = Name,
        Baseline = Baseline?.DuplicateCurve(),
        BaseElevation = BaseElevation,
        Height = Height,
        Justification = Justification,
        Flipped = Flipped,
        GroupIndex = -1
      };
      foreach (var o in Openings)
      {
        var c = o.Duplicate(newId);
        c.WallId = w.Id;
        w.Openings.Add(c);
      }
      return w;
    }

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "assemblyId", AssemblyId);
      Ark.Put(d, "name", Name);
      Ark.PutCurve(d, "baseline", Baseline);
      Ark.Put(d, "baseElevation", BaseElevation);
      Ark.Put(d, "height", Height);
      Ark.PutEnum(d, "justification", Justification);
      Ark.Put(d, "flipped", Flipped);
      Ark.Put(d, "groupIndex", GroupIndex);
      Ark.Put(d, "objectIds", string.Join(" ", LayerObjectIds.Select(g => g.ToString("N"))));
      Ark.PutList(d, "openings", Openings.Select(o => o.ToDictionary()).ToList());
      return d;
    }

    public static WallDefinition FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      var w = new WallDefinition
      {
        Id = Ark.Id(d, "id"),
        AssemblyId = Ark.Id(d, "assemblyId"),
        Name = Ark.Str(d, "name"),
        Baseline = Ark.GetCurve(d, "baseline"),
        BaseElevation = Ark.Num(d, "baseElevation"),
        Height = Ark.Num(d, "height", 8.0),
        Justification = Ark.Enum(d, "justification", AssemblyJustification.CoreCenter),
        Flipped = Ark.Bool(d, "flipped"),
        GroupIndex = Ark.Int(d, "groupIndex", -1)
      };

      var ids = Ark.Str(d, "objectIds");
      foreach (var token in ids.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        if (Guid.TryParse(token, out var g)) w.LayerObjectIds.Add(g);

      foreach (var od in Ark.List(d, "openings"))
      {
        var o = Opening.FromDictionary(od);
        if (o != null) { o.WallId = w.Id; w.Openings.Add(o); }
      }
      return w;
    }
  }
}
