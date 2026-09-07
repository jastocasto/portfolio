using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// An ordered stack of material layers. Layers are always stored
  /// EXTERIOR FIRST -> INTERIOR LAST, which matches how wall types are drawn in
  /// details and how they are read on a section cut.
  /// </summary>
  public class WallAssembly
  {
    public Guid Id = Guid.NewGuid();

    /// <summary>Short code used on drawings and in the wall type tag, e.g. "W1".</summary>
    public string Code = "W1";
    public string Name = "New wall type";
    public string Description = "";

    /// <summary>Fire resistance rating in hours, 0 = unrated.</summary>
    public double FireRatingHours = 0.0;

    /// <summary>Assembly STC rating, 0 = not established.</summary>
    public int StcRating = 0;

    public List<AssemblyLayer> Layers = new List<AssemblyLayer>();

    // ---- derived values ----------------------------------------------------

    public IEnumerable<AssemblyLayer> ActiveLayers => Layers.Where(l => l.Enabled);

    /// <summary>Total thickness in inches.</summary>
    public double TotalThicknessIn => ActiveLayers.Sum(l => Math.Max(0.0, l.ThicknessIn));

    /// <summary>Index of the structural core inside <see cref="Layers"/>.
    /// Falls back to the first structural layer, then to the thickest layer.</summary>
    public int CoreIndex
    {
      get
      {
        if (Layers.Count == 0) return -1;
        int i = Layers.FindIndex(l => l.Enabled && l.IsCore);
        if (i >= 0) return i;
        i = Layers.FindIndex(l => l.Enabled && l.Function == LayerFunction.Structure);
        if (i >= 0) return i;
        double best = double.MinValue; int bestIdx = -1;
        for (int k = 0; k < Layers.Count; k++)
        {
          if (!Layers[k].Enabled) continue;
          if (Layers[k].ThicknessIn > best) { best = Layers[k].ThicknessIn; bestIdx = k; }
        }
        return bestIdx;
      }
    }

    public AssemblyLayer Core
    {
      get { int i = CoreIndex; return (i >= 0 && i < Layers.Count) ? Layers[i] : null; }
    }

    /// <summary>Nominal assembly R-value: layer R plus interior and exterior air films.
    /// This is a layer-sum (series) value; it does not account for thermal bridging
    /// through framing - use <see cref="EffectiveRValue"/> for that.</summary>
    public double RValue(AssemblyCatalog catalog)
    {
      double r = 0.17 + 0.68;   // exterior + interior still-air films (ASHRAE, vertical surface)
      foreach (var l in ActiveLayers)
      {
        var p = catalog?.FindProduct(l.ProductId);
        r += p?.RValueAt(l.ThicknessIn) ?? 0.0;
        var cavity = catalog?.FindProduct(l.CavityProductId);
        if (cavity != null) r += cavity.RValueAt(l.ThicknessIn);
      }
      return r;
    }

    /// <summary>Parallel-path corrected R-value using the framing factor of the core layer.
    /// framingFraction defaults to 0.25 (16 in o.c. wood framing with headers and plates).</summary>
    public double EffectiveRValue(AssemblyCatalog catalog, double framingFraction = 0.25)
    {
      var core = Core;
      if (core == null || core.Function != LayerFunction.Structure || framingFraction <= 0.0)
        return RValue(catalog);

      double rContinuous = 0.17 + 0.68;
      double rCavity = 0.0, rFraming = 0.0;

      foreach (var l in ActiveLayers)
      {
        var p = catalog?.FindProduct(l.ProductId);
        double r = p?.RValueAt(l.ThicknessIn) ?? 0.0;
        if (ReferenceEquals(l, core))
        {
          var cavity = catalog?.FindProduct(l.CavityProductId);
          rCavity += r + (cavity?.RValueAt(l.ThicknessIn) ?? 0.0);
          // Softwood framing is about R-1.25 per inch; light-gauge steel is a
          // thermal short circuit and is handled by the framing factor instead.
          rFraming += 1.25 * l.ThicknessIn;
        }
        else rContinuous += r;
      }

      double pathCavity = rContinuous + rCavity;
      double pathFraming = rContinuous + rFraming;
      if (pathCavity <= 0 || pathFraming <= 0) return RValue(catalog);

      double u = (1.0 - framingFraction) / pathCavity + framingFraction / pathFraming;
      return u > 0 ? 1.0 / u : RValue(catalog);
    }

    /// <summary>Installed cost per square foot of wall face, waste included.</summary>
    public double CostPerSqFt(AssemblyCatalog catalog)
    {
      double c = 0.0;
      foreach (var l in ActiveLayers)
      {
        var p = catalog?.FindProduct(l.ProductId);
        if (p != null) c += p.CostPerSqFt * (1.0 + Math.Max(0.0, p.WasteFactor));
        var cavity = catalog?.FindProduct(l.CavityProductId);
        if (cavity != null) c += cavity.CostPerSqFt * (1.0 + Math.Max(0.0, cavity.WasteFactor));
      }
      return c;
    }

    /// <summary>Self weight in pounds per square foot.</summary>
    public double WeightPsf(AssemblyCatalog catalog)
    {
      double w = 0.0;
      foreach (var l in ActiveLayers)
      {
        var p = catalog?.FindProduct(l.ProductId);
        if (p != null && p.DensityPcf > 0.0) w += p.DensityPcf * (l.ThicknessIn / 12.0);
        var cavity = catalog?.FindProduct(l.CavityProductId);
        if (cavity != null && cavity.DensityPcf > 0.0) w += cavity.DensityPcf * (l.ThicknessIn / 12.0);
      }
      return w;
    }

    /// <summary>Distance in inches from the exterior face to the start of layer i.</summary>
    public double StationOf(int layerIndex)
    {
      double u = 0.0;
      for (int i = 0; i < layerIndex && i < Layers.Count; i++)
      {
        if (!Layers[i].Enabled) continue;
        u += Math.Max(0.0, Layers[i].ThicknessIn);
      }
      return u;
    }

    /// <summary>Distance in inches from the exterior face to the baseline, for a
    /// given justification. This is the whole trick: because the baseline station
    /// is recomputed from the current layer thicknesses, a CoreCenter wall keeps
    /// its structure exactly where it was drawn and every other layer grows
    /// outward from it.</summary>
    public double BaselineStation(WallJustification justification)
    {
      int core = CoreIndex;
      double total = TotalThicknessIn;
      switch (justification)
      {
        case WallJustification.ExteriorFace: return 0.0;
        case WallJustification.InteriorFace: return total;
        case WallJustification.WallCenter: return total * 0.5;
        case WallJustification.ExteriorCore: return core >= 0 ? StationOf(core) : total * 0.5;
        case WallJustification.InteriorCore:
          return core >= 0 ? StationOf(core) + Math.Max(0.0, Layers[core].ThicknessIn) : total * 0.5;
        case WallJustification.CoreCenter:
        default:
          return core >= 0 ? StationOf(core) + Math.Max(0.0, Layers[core].ThicknessIn) * 0.5 : total * 0.5;
      }
    }

    public void NormalizeSides()
    {
      int core = CoreIndex;
      for (int i = 0; i < Layers.Count; i++)
      {
        if (core < 0) { Layers[i].Side = LayerSide.Exterior; continue; }
        Layers[i].Side = i < core ? LayerSide.Exterior : (i == core ? LayerSide.Core : LayerSide.Interior);
        Layers[i].IsCore = (i == core);
      }
    }

    public WallAssembly Duplicate(bool newId = true)
    {
      var a = new WallAssembly
      {
        Id = newId ? Guid.NewGuid() : Id,
        Code = Code,
        Name = Name,
        Description = Description,
        FireRatingHours = FireRatingHours,
        StcRating = StcRating,
        Layers = Layers.Select(l => l.Duplicate(newId)).ToList()
      };
      return a;
    }

    public override string ToString() => Code + " - " + Name;

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "code", Code);
      Ark.Put(d, "name", Name);
      Ark.Put(d, "description", Description);
      Ark.Put(d, "fireRating", FireRatingHours);
      Ark.Put(d, "stc", StcRating);
      Ark.PutList(d, "layers", Layers.Select(l => l.ToDictionary()).ToList());
      return d;
    }

    public static WallAssembly FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      var a = new WallAssembly
      {
        Id = Ark.Id(d, "id"),
        Code = Ark.Str(d, "code", "W?"),
        Name = Ark.Str(d, "name", "Wall type"),
        Description = Ark.Str(d, "description"),
        FireRatingHours = Ark.Num(d, "fireRating"),
        StcRating = Ark.Int(d, "stc")
      };
      foreach (var ld in Ark.List(d, "layers"))
      {
        var layer = AssemblyLayer.FromDictionary(ld);
        if (layer != null) a.Layers.Add(layer);
      }
      return a;
    }
  }
}
