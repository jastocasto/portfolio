using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Collections;
using Rhino.Geometry;

namespace Stratum.Core
{
  /// <summary>
  /// What a wall, floor and roof have in common: a layered assembly, a rule for
  /// where the reference sits inside it, and the Rhino objects it was baked into.
  ///
  /// Walls predate this and keep their own class, because a wall's parameters
  /// (a baseline, a top condition, hosted openings) are genuinely different. Floors
  /// and roofs are close enough to share.
  /// </summary>
  public abstract class LayeredElement
  {
    public Guid Id = Guid.NewGuid();
    public Guid AssemblyId = Guid.Empty;
    public string Name = "";

    /// <summary>Where the reference plane or surface sits inside the layer stack.
    /// Uses the same enum as walls; AssemblyNaming words it per kind, so a floor
    /// reads "Top face" where a wall reads "Exterior face".</summary>
    public AssemblyJustification Justification = AssemblyJustification.FirstCore;

    public List<Guid> LayerObjectIds = new List<Guid>();
    public int GroupIndex = -1;

    public abstract string Prefix { get; }

    public string GroupName => Prefix + " " + Id.ToString("N").Substring(0, 8);

    public string DisplayName => string.IsNullOrEmpty(Name) ? GroupName : Name;

    protected void WriteBase(ArchivableDictionary d)
    {
      Ark.Put(d, "id", Id);
      Ark.Put(d, "assemblyId", AssemblyId);
      Ark.Put(d, "name", Name);
      Ark.PutEnum(d, "justification", Justification);
      Ark.Put(d, "groupIndex", GroupIndex);
      Ark.Put(d, "objectIds", string.Join(" ", LayerObjectIds.Select(g => g.ToString("N"))));
    }

    protected void ReadBase(ArchivableDictionary d)
    {
      Id = Ark.Id(d, "id");
      AssemblyId = Ark.Id(d, "assemblyId");
      Name = Ark.Str(d, "name");
      Justification = Ark.Enum(d, "justification", AssemblyJustification.FirstCore);
      GroupIndex = Ark.Int(d, "groupIndex", -1);

      foreach (var token in Ark.Str(d, "objectIds")
                               .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        if (Guid.TryParse(token, out var g)) LayerObjectIds.Add(g);
    }
  }

  /// <summary>
  /// A floor, ceiling or flat slab: a horizontal layered element bounded by a closed
  /// curve, with optional holes for stair wells and chases.
  ///
  /// The layers stack downward from the reference plane by default, because the
  /// reference is normally the top of the structural core - top of joists, top of
  /// slab - which is what framing is set out from.
  /// </summary>
  public class SlabDefinition : LayeredElement
  {
    public override string Prefix => "Floor";

    /// <summary>Outer boundary, a closed planar curve in model space.</summary>
    public Curve Boundary;

    /// <summary>Closed curves cut out of the slab: stair wells, shafts, chases.</summary>
    public List<Curve> Holes = new List<Curve>();

    public Guid LevelId = Guid.Empty;

    /// <summary>Height above the level the reference plane sits at, model units.</summary>
    public double Offset = 0.0;

    /// <summary>Resolved elevation of the reference plane, model units on world Z.</summary>
    public double Elevation = 0.0;

    public void Resolve(BimModel model)
    {
      var level = model?.FindLevel(LevelId);
      if (level != null) Elevation = level.Elevation + Offset;
    }

    /// <summary>Plan area inside the boundary, less the holes, in model units squared.</summary>
    public double PlanArea
    {
      get
      {
        if (Boundary == null) return 0.0;
        double area = AreaOf(Boundary);
        foreach (var hole in Holes) area -= AreaOf(hole);
        return Math.Max(0.0, area);
      }
    }

    static double AreaOf(Curve curve)
    {
      if (curve == null || !curve.IsClosed) return 0.0;
      var props = AreaMassProperties.Compute(curve);
      return props?.Area ?? 0.0;
    }

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      WriteBase(d);
      Ark.PutCurve(d, "boundary", Boundary);
      Ark.Put(d, "holeCount", Holes.Count);
      for (int i = 0; i < Holes.Count; i++) Ark.PutCurve(d, "hole" + i, Holes[i]);
      Ark.Put(d, "levelId", LevelId);
      Ark.Put(d, "offset", Offset);
      Ark.Put(d, "elevation", Elevation);
      return d;
    }

    public static SlabDefinition FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      var slab = new SlabDefinition();
      slab.ReadBase(d);
      slab.Boundary = Ark.GetCurve(d, "boundary");

      int holes = Ark.Int(d, "holeCount");
      for (int i = 0; i < holes; i++)
      {
        var hole = Ark.GetCurve(d, "hole" + i);
        if (hole != null) slab.Holes.Add(hole);
      }

      slab.LevelId = Ark.Id(d, "levelId");
      slab.Offset = Ark.Num(d, "offset");
      slab.Elevation = Ark.Num(d, "elevation");
      return slab;
    }
  }

  /// <summary>
  /// A roof: a layered assembly built off a surface you drew.
  ///
  /// Stratum does not generate roof forms. You model the shape - which you are doing
  /// anyway, since the same surface can cap the walls underneath it - and the layers
  /// are offset off it. That way hips, valleys, dormers and curved roofs all work,
  /// because they are your geometry rather than something a generator had to
  /// anticipate.
  /// </summary>
  public class RoofDefinition : LayeredElement
  {
    public override string Prefix => "Roof";

    /// <summary>The surface or polysurface the layers are built from. Stored by
    /// object id and re-read on rebuild, so editing the form and running BimRebuild
    /// regenerates the roof.</summary>
    public Guid SurfaceObjectId = Guid.Empty;

    public RoofDefinition()
    {
      // A roof surface is most naturally drawn at the top of the structural deck -
      // top of rafters. That is also the plane a wall raking into the roof should
      // stop at, so one surface can drive the roof layers and the walls beneath it.
      Justification = AssemblyJustification.FirstCore;
    }

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      WriteBase(d);
      Ark.Put(d, "surface", SurfaceObjectId);
      return d;
    }

    public static RoofDefinition FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      var roof = new RoofDefinition();
      roof.ReadBase(d);
      roof.SurfaceObjectId = Ark.Id(d, "surface");
      return roof;
    }
  }
}
