using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
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

    /// <summary>Level the wall is built from. Empty for a free-standing wall that
    /// carries its own elevation.</summary>
    public Guid LevelId = Guid.Empty;

    /// <summary>Height above the level the wall starts at, model units.</summary>
    public double BaseOffset = 0.0;

    /// <summary>Bottom of wall, model units, measured on world Z. Resolved from the
    /// level and offset by <see cref="Resolve"/> before every build; it is the value
    /// the geometry is actually generated from.</summary>
    public double BaseElevation = 0.0;

    /// <summary>Wall height, model units. Used directly when <see cref="TopMode"/>
    /// is Height, and as the fallback when a level or surface cannot be resolved.</summary>
    public double Height = 8.0;

    public WallTopMode TopMode = WallTopMode.Height;

    /// <summary>Level the wall builds up to when TopMode is ToLevel.</summary>
    public Guid TopLevelId = Guid.Empty;

    /// <summary>Offset from the top level, model units. Negative to stop below it -
    /// a wall to underside of structure rather than to the floor above.</summary>
    public double TopOffset = 0.0;

    /// <summary>Surface or polysurface the wall is cut against when TopMode is
    /// ToSurface. Stored by object id and re-read on every rebuild, so editing the
    /// roof and rebuilding re-cuts the walls under it.</summary>
    public Guid TopSurfaceObjectId = Guid.Empty;

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

    /// <summary>
    /// Resolves the level bindings into the plain elevation and height the geometry
    /// is built from. Called before every build, so moving a level moves its walls.
    ///
    /// ToSurface is not resolved here: the height for that case depends on the
    /// target geometry and is worked out by the builder, which has the document.
    /// </summary>
    public void Resolve(BimModel model)
    {
      if (model == null) return;

      var level = model.FindLevel(LevelId);
      if (level != null) BaseElevation = level.Elevation + BaseOffset;

      if (TopMode != WallTopMode.ToLevel) return;

      var top = model.FindLevel(TopLevelId);
      if (top == null) return;

      double height = (top.Elevation + TopOffset) - BaseElevation;
      if (height > RhinoMath.ZeroTolerance) Height = height;
    }

    public double Length => Baseline?.GetLength() ?? 0.0;

    public WallDefinition Duplicate(bool newId = true)
    {
      var w = new WallDefinition
      {
        Id = newId ? Guid.NewGuid() : Id,
        AssemblyId = AssemblyId,
        Name = Name,
        Baseline = Baseline?.DuplicateCurve(),
        LevelId = LevelId,
        BaseOffset = BaseOffset,
        BaseElevation = BaseElevation,
        Height = Height,
        TopMode = TopMode,
        TopLevelId = TopLevelId,
        TopOffset = TopOffset,
        TopSurfaceObjectId = TopSurfaceObjectId,
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
      Ark.Put(d, "levelId", LevelId);
      Ark.Put(d, "baseOffset", BaseOffset);
      Ark.Put(d, "baseElevation", BaseElevation);
      Ark.Put(d, "height", Height);
      Ark.PutEnum(d, "topMode", TopMode);
      Ark.Put(d, "topLevelId", TopLevelId);
      Ark.Put(d, "topOffset", TopOffset);
      Ark.Put(d, "topSurface", TopSurfaceObjectId);
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
        LevelId = Ark.Id(d, "levelId"),
        BaseOffset = Ark.Num(d, "baseOffset"),
        BaseElevation = Ark.Num(d, "baseElevation"),
        Height = Ark.Num(d, "height", 8.0),
        TopMode = Ark.Enum(d, "topMode", WallTopMode.Height),
        TopLevelId = Ark.Id(d, "topLevelId"),
        TopOffset = Ark.Num(d, "topOffset"),
        TopSurfaceObjectId = Ark.Id(d, "topSurface"),
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
