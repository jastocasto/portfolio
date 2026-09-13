using System;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Stratum.Core;

namespace Stratum.Documents
{
  /// <summary>
  /// Files a material layer onto the layer the document already has for it.
  ///
  /// Stratum's own tree — Stratum::Walls::W1::06 Wood stud 2x6 — is a good index
  /// and a bad filing system. Line weight, colour and print control are all driven
  /// off layers, so geometry that sits outside the office layer standard cannot be
  /// controlled from a sheet. This maps each layer onto the standard instead.
  ///
  /// **It never creates a layer.** If the document has no layer of that name — a
  /// blank file, someone else's template — it returns -1 and the caller falls back
  /// to Stratum's tree. So the plug-in still works anywhere, and in a tds document
  /// the walls land where the drawings are controlled from.
  ///
  /// Matching is on the product name, because that is what carries the material.
  /// LayerFunction alone cannot separate OSB from plywood, or a rafter from a truss.
  /// </summary>
  internal static class TemplateLayers
  {
    /// <summary>Index of the document layer this material belongs on, or -1.</summary>
    internal static int Resolve(RhinoDoc doc, LayeredAssembly assembly,
                                int layerIndex, AssemblyLayer layer, MaterialProduct product)
    {
      if (doc == null || layer == null) return -1;

      string name = NameFor(assembly, layerIndex, layer, product);
      if (string.IsNullOrEmpty(name)) return -1;

      foreach (var existing in doc.Layers)
      {
        if (existing == null || existing.IsDeleted) continue;
        if (existing.ParentLayerId != Guid.Empty) continue;     // the standard is flat
        if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
          return existing.Index;
      }
      return -1;
    }

    // ---------------------------------------------------------------- the map

    static string NameFor(LayeredAssembly assembly, int layerIndex,
                          AssemblyLayer layer, MaterialProduct product)
    {
      string n = (product?.Name ?? layer.ProductName ?? "").ToLowerInvariant();
      var kind = assembly?.Kind ?? AssemblyKind.Wall;

      switch (layer.Function)
      {
        case LayerFunction.Membrane:   return Membrane(kind, n);
        case LayerFunction.Insulation: return Outboard(assembly, layerIndex, layer)
                                              ? "Env-Barr-IzoExt" : "Env-Barr-IzoInt";
        case LayerFunction.AirGap:     return "Env-Barr-Rainscreen";
        case LayerFunction.Furring:    return "Env-Barr-Battens";
        case LayerFunction.Sheathing:  return Sheathing(n);
        case LayerFunction.Cladding:   return Cladding(kind, n);
        case LayerFunction.Structure:  return Structure(kind, n);
        case LayerFunction.Finish:     return Finish(kind, n);
      }
      return null;
    }

    static string Membrane(AssemblyKind kind, string n)
    {
      if (Has(n, "waterproof")) return "Env-Barr-WaterproofInt";
      // Under-slab polyethylene is a ground-damp barrier, not an air barrier.
      if (kind == AssemblyKind.Floor) return "Env-Barr-WaterproofInt";
      if (Has(n, "wrb", "housewrap", "underlayment", "weather")) return "Env-Barr-WRB";
      if (Has(n, "vapour", "vapor", "air barrier")) return "Env-Barr-Air";
      return "Env-Barr-Air";
    }

    static string Sheathing(string n)
    {
      if (Has(n, "osb")) return "Struct-Shth-OSB";
      if (Has(n, "plywood", "ply ", "cdx", "subfloor")) return "Struct-Shth-Ply";
      if (Has(n, "gypsum", "densglass", "glass mat")) return "Struct-Shth-Gyp";
      return "Struct-Shth-Ply";
    }

    static string Cladding(AssemblyKind kind, string n)
    {
      if (kind == AssemblyKind.Roof)
      {
        if (Has(n, "shingle")) return "Env-Roof-Shingles";
        if (Has(n, "clay", "tile"))  return "Env-Roof-ClayTiles";
        if (Has(n, "tpo", "epdm", "single-ply", "membrane")) return "Env-Roof-TPO";
        if (Has(n, "metal", "standing seam", "steel", "zinc", "copper")) return "Env-Roof-Metal";
        return "Env-Roof-Shingles";
      }
      if (Has(n, "brick")) return "Env-Wall-Brick";
      if (Has(n, "stone", "limestone", "masonry veneer")) return "Env-Wall-Stone";
      if (Has(n, "stucco", "render", "lime plaster")) return "Env-Wall-Stucco";
      if (Has(n, "metal", "steel", "zinc", "aluminium", "aluminum")) return "Env-Wall-Metal";
      if (Has(n, "concrete", "precast")) return "Env-Wall-Concrete";
      // Fiber cement lap siding files with wood: same profile, same trim, same detail.
      return "Env-Wall-Wood";
    }

    static string Structure(AssemblyKind kind, string n)
    {
      if (kind == AssemblyKind.Roof)
      {
        if (Has(n, "truss"))  return "Struct-Roof-Trusses";
        if (Has(n, "rafter")) return "Struct-Roof-Rafters";
        if (Has(n, "joist"))  return "Struct-Roof-Joists";
        if (Has(n, "beam", "purlin", "ridge")) return "Struct-Roof-Beams";
        return "Struct-Roof-Core";
      }
      if (kind == AssemblyKind.Floor)
      {
        if (Has(n, "gravel", "aggregate", "hardcore")) return "Site-Gravel";
        if (Has(n, "slab", "concrete")) return "Struct-Fndtn-Slab";
        if (Has(n, "joist")) return "Struct-Floor-Joists";
        if (Has(n, "beam", "girder")) return "Struct-Floor-Beams";
        return "Struct-Floor-Joists";
      }
      if (Has(n, "steel stud", "metal stud", "ga @", "gauge")) return "Struct-Wall-MtlStud";
      if (Has(n, "stud"))    return "Struct-Wall-WdStud";
      if (Has(n, "cmu", "block")) return "Struct-Wall-Core";
      if (Has(n, "concrete")) return "Struct-Wall-Concrete";
      if (Has(n, "brick"))   return "Struct-Wall-Brick";
      if (Has(n, "stone"))   return "Struct-Wall-Stone";
      if (Has(n, "column", "post")) return "Struct-Wall-Column";
      return "Struct-Wall-Core";
    }

    static string Finish(AssemblyKind kind, string n)
    {
      // Gypsum on the underside of a floor or roof is the ceiling.
      if (kind != AssemblyKind.Wall && Has(n, "gypsum", "plaster", "drywall"))
        return "Int-Ceiling-Finish";

      if (kind == AssemblyKind.Floor)
      {
        if (Has(n, "laminate", "vinyl", "lvt")) return "Int-Floor-Laminate";
        if (Has(n, "tile", "porcelain", "ceramic", "terrazzo")) return "Int-Floor-Tile";
        if (Has(n, "concrete", "polished")) return "Int-Floor-Concrete";
        if (Has(n, "threshold")) return "Int-Floor-Threshold";
        return "Int-Floor-Wood";
      }

      if (Has(n, "gypsum", "plaster", "drywall")) return "Int-Wall-Plaster";
      if (Has(n, "tile"))  return "Int-Wall-Tile";
      if (Has(n, "paint")) return "Int-Wall-Paint";
      return "Int-Wall-Covering";
    }

    // ------------------------------------------------------------- helpers

    /// <summary>True when this layer sits outboard of the assembly's structural core.
    /// Read off the core's position rather than <see cref="AssemblyLayer.Side"/>, which
    /// is advisory and not set on every catalogue entry.</summary>
    static bool Outboard(LayeredAssembly assembly, int layerIndex, AssemblyLayer layer)
    {
      if (assembly == null || assembly.Layers == null || assembly.Layers.Count == 0)
        return layer.Side == LayerSide.Outer;

      int core = assembly.Layers.FindIndex(l => l != null && l.IsCore);
      if (core < 0)
        core = assembly.Layers.FindIndex(l => l != null && l.Function == LayerFunction.Structure);
      if (core < 0)
        return layer.Side == LayerSide.Outer;

      return layerIndex < core;
    }

    static bool Has(string haystack, params string[] needles)
      => needles.Any(x => haystack.IndexOf(x, StringComparison.Ordinal) >= 0);
  }
}
