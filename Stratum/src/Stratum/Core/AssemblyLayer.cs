using System;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// One material layer inside a wall assembly. Each layer becomes its own
  /// closed solid in the model, so a section cut through the wall shows exactly
  /// what gets built.
  /// </summary>
  public class AssemblyLayer
  {
    public Guid Id = Guid.NewGuid();

    /// <summary>Product this layer is made of. May be Guid.Empty for a raw
    /// air gap that is not tied to a purchased product.</summary>
    public Guid ProductId = Guid.Empty;

    /// <summary>Cached product name, so an assembly still reads correctly if the
    /// catalog entry is missing.</summary>
    public string ProductName = "";

    /// <summary>Insulation that sits INSIDE a framed core, in the same physical
    /// space as the studs. It adds no thickness, but it carries R-value, cost and
    /// weight, and it is what makes the parallel-path thermal math correct.</summary>
    public Guid CavityProductId = Guid.Empty;
    public string CavityProductName = "";

    public LayerFunction Function = LayerFunction.Finish;
    public LayerSide Side = LayerSide.Inner;

    /// <summary>
    /// Priority given to this layer explicitly, or 0 to take it from
    /// <see cref="Function"/>. Almost every layer should leave this at 0; set it
    /// only where one assembly disagrees with the default for its function.
    /// </summary>
    public int PriorityOverride = 0;

    /// <summary>
    /// Who wins where two layers meet at a junction: the lower number runs
    /// through, the higher one stops against its face.
    ///
    /// This is Revit's rule, and it is the same rule House Anatomy's GAPS.md
    /// states in words - the studs go through until they touch studs, the gyp
    /// until it meets the first instance of gyp.
    /// </summary>
    public int Priority => PriorityOverride > 0 ? PriorityOverride : DefaultPriority(Function);

    /// <summary>
    /// A membrane never wins a junction and never wraps one. It is a surface
    /// applied to whatever it covers, so it stops at the first thing it reaches,
    /// whatever that is. Revit models this by giving membranes no priority at
    /// all; a number larger than every real layer's has the same effect without
    /// a special case at every comparison.
    /// </summary>
    public const int MembranePriority = 99;

    /// <summary>The priority a layer carries by virtue of what it does. Numbered
    /// on Revit's 1-5 scale so the two systems can be reasoned about together.</summary>
    public static int DefaultPriority(LayerFunction function)
    {
      switch (function)
      {
        case LayerFunction.Structure:  return 1;   // studs, CMU, concrete
        case LayerFunction.Sheathing:  return 2;   // substrate
        case LayerFunction.Insulation: return 3;   // thermal / air
        case LayerFunction.AirGap:     return 3;
        case LayerFunction.Furring:    return 4;   // finish 1
        case LayerFunction.Cladding:   return 4;
        case LayerFunction.Finish:     return 5;   // finish 2
        case LayerFunction.Membrane:   return MembranePriority;
        default:                       return 5;
      }
    }

    public bool IsMembrane => Function == LayerFunction.Membrane;

    /// <summary>Installed thickness in inches. Defaults to the product thickness
    /// but may be overridden per assembly (e.g. 2x6 cavity filled to 5-1/2 in).</summary>
    public double ThicknessIn = 0.5;

    /// <summary>True for the structural core. Exactly one layer per assembly is
    /// the core; its centre line is the wall's reference line by default.</summary>
    public bool IsCore = false;

    /// <summary>Layers may be switched off without being deleted (e.g. a
    /// design option with no rainscreen).</summary>
    public bool Enabled = true;

    /// <summary>Structural layers stay put when a neighbouring layer changes
    /// thickness; non-structural layers push the wall face outward.</summary>
    public bool IsLoadBearing = false;

    // ---- how this layer terminates at an opening ---------------------------

    public EdgeResolution JambResolution = EdgeResolution.Butt;
    public EdgeResolution HeadResolution = EdgeResolution.Butt;
    public EdgeResolution SillResolution = EdgeResolution.Butt;

    /// <summary>Depth in inches that the layer wraps into / is held back from the
    /// reveal. Positive means material remains inside the rough opening.</summary>
    public double JambReturnIn = 0.0;
    public double HeadReturnIn = 0.0;
    public double SillReturnIn = 0.0;

    public string Notes = "";

    public AssemblyLayer Duplicate(bool newId = true)
    {
      var l = (AssemblyLayer)MemberwiseClone();
      if (newId) l.Id = Guid.NewGuid();
      return l;
    }

    /// <summary>Signed inset applied to the opening cutter for this layer.
    /// Positive shrinks the cut (material wraps in); negative enlarges it.</summary>
    public double EdgeInset(EdgeResolution resolution, double returnIn)
    {
      switch (resolution)
      {
        case EdgeResolution.Wrap: return Math.Max(0.0, returnIn);
        case EdgeResolution.ReturnToFrame: return Math.Max(0.0, returnIn);
        case EdgeResolution.HoldBack: return -Math.Abs(returnIn);
        case EdgeResolution.Butt:
        default: return 0.0;
      }
    }

    public double JambInset => EdgeInset(JambResolution, JambReturnIn);
    public double HeadInset => EdgeInset(HeadResolution, HeadReturnIn);
    public double SillInset => EdgeInset(SillResolution, SillReturnIn);

    public bool CutAtOpenings =>
      !(JambResolution == EdgeResolution.Continuous &&
        HeadResolution == EdgeResolution.Continuous &&
        SillResolution == EdgeResolution.Continuous);

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "productId", ProductId);
      Ark.Put(d, "productName", ProductName);
      Ark.Put(d, "cavityProductId", CavityProductId);
      Ark.Put(d, "cavityProductName", CavityProductName);
      Ark.PutEnum(d, "function", Function);
      Ark.PutEnum(d, "side", Side);
      Ark.Put(d, "thicknessIn", ThicknessIn);
      Ark.Put(d, "isCore", IsCore);
      Ark.Put(d, "enabled", Enabled);
      Ark.Put(d, "loadBearing", IsLoadBearing);
      Ark.Put(d, "priority", PriorityOverride);
      Ark.PutEnum(d, "jambRes", JambResolution);
      Ark.PutEnum(d, "headRes", HeadResolution);
      Ark.PutEnum(d, "sillRes", SillResolution);
      Ark.Put(d, "jambReturn", JambReturnIn);
      Ark.Put(d, "headReturn", HeadReturnIn);
      Ark.Put(d, "sillReturn", SillReturnIn);
      Ark.Put(d, "notes", Notes);
      return d;
    }

    public static AssemblyLayer FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      return new AssemblyLayer
      {
        Id = Ark.Id(d, "id"),
        ProductId = Ark.Id(d, "productId"),
        ProductName = Ark.Str(d, "productName"),
        CavityProductId = Ark.Id(d, "cavityProductId"),
        CavityProductName = Ark.Str(d, "cavityProductName"),
        Function = Ark.Enum(d, "function", LayerFunction.Finish),
        Side = Ark.Enum(d, "side", LayerSide.Inner),
        ThicknessIn = Ark.Num(d, "thicknessIn", 0.5),
        IsCore = Ark.Bool(d, "isCore"),
        Enabled = Ark.Bool(d, "enabled", true),
        IsLoadBearing = Ark.Bool(d, "loadBearing"),
        // Absent in documents written before priority existed; 0 means "use the
        // default for this layer's function", which is what those files want.
        PriorityOverride = (int)Ark.Num(d, "priority", 0.0),
        JambResolution = Ark.Enum(d, "jambRes", EdgeResolution.Butt),
        HeadResolution = Ark.Enum(d, "headRes", EdgeResolution.Butt),
        SillResolution = Ark.Enum(d, "sillRes", EdgeResolution.Butt),
        JambReturnIn = Ark.Num(d, "jambReturn"),
        HeadReturnIn = Ark.Num(d, "headReturn"),
        SillReturnIn = Ark.Num(d, "sillReturn"),
        Notes = Ark.Str(d, "notes")
      };
    }
  }
}
