using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Collections;
using Rhino.FileIO;

namespace Stratum.Core
{
  /// <summary>
  /// The product catalog and the wall type library.
  ///
  /// A catalog lives in two places at once:
  ///  * inside every .3dm, so a file always opens with everything it needs;
  ///  * in a shared office library on disk, so wall types can be reused between
  ///    projects and kept in step with current pricing.
  /// </summary>
  public class AssemblyCatalog
  {
    public List<MaterialProduct> Products = new List<MaterialProduct>();
    public List<LayeredAssembly> Assemblies = new List<LayeredAssembly>();

    /// <summary>Window and door types.</summary>
    public List<OpeningUnit> OpeningUnits = new List<OpeningUnit>();

    public MaterialProduct FindProduct(Guid id)
      => id == Guid.Empty ? null : Products.FirstOrDefault(p => p.Id == id);

    public MaterialProduct FindProductByName(string name)
      => string.IsNullOrEmpty(name) ? null
         : Products.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public LayeredAssembly FindAssembly(Guid id)
      => id == Guid.Empty ? null : Assemblies.FirstOrDefault(a => a.Id == id);

    public LayeredAssembly FindAssemblyByCode(string code)
      => string.IsNullOrEmpty(code) ? null
         : Assemblies.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));

    public OpeningUnit FindUnit(Guid id)
      => id == Guid.Empty ? null : OpeningUnits.FirstOrDefault(u => u.Id == id);

    public IEnumerable<OpeningUnit> UnitsOfKind(OpeningKind kind)
      => OpeningUnits.Where(u => u.Kind == kind);

    public IEnumerable<string> Categories
      => Products.Select(p => p.Category).Distinct().OrderBy(c => c);

    /// <summary>Merges another catalog in. Existing ids win unless overwrite is set.</summary>
    public void Merge(AssemblyCatalog other, bool overwrite = false)
    {
      if (other == null) return;
      foreach (var p in other.Products)
      {
        var existing = FindProduct(p.Id);
        if (existing == null) Products.Add(p);
        else if (overwrite) { Products.Remove(existing); Products.Add(p); }
      }
      foreach (var a in other.Assemblies)
      {
        var existing = FindAssembly(a.Id);
        if (existing == null) Assemblies.Add(a);
        else if (overwrite) { Assemblies.Remove(existing); Assemblies.Add(a); }
      }
      foreach (var u in other.OpeningUnits)
      {
        var existing = FindUnit(u.Id);
        if (existing == null) OpeningUnits.Add(u);
        else if (overwrite) { OpeningUnits.Remove(existing); OpeningUnits.Add(u); }
      }
    }

    /// <summary>Ensures a layer's cached product name matches the catalog.</summary>
    public void SyncLayerNames()
    {
      foreach (var a in Assemblies)
        foreach (var l in a.Layers)
        {
          var p = FindProduct(l.ProductId);
          if (p != null) l.ProductName = p.Name;
          var cavity = FindProduct(l.CavityProductId);
          l.CavityProductName = cavity?.Name ?? "";
        }
    }

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "schema", 1);
      Ark.PutList(d, "products", Products.Select(p => p.ToDictionary()).ToList());
      Ark.PutList(d, "assemblies", Assemblies.Select(a => a.ToDictionary()).ToList());
      Ark.PutList(d, "openingUnits", OpeningUnits.Select(u => u.ToDictionary()).ToList());
      return d;
    }

    public static AssemblyCatalog FromDictionary(ArchivableDictionary d)
    {
      var c = new AssemblyCatalog();
      if (d == null) return c;
      foreach (var pd in Ark.List(d, "products"))
      {
        var p = MaterialProduct.FromDictionary(pd);
        if (p != null) c.Products.Add(p);
      }
      foreach (var ad in Ark.List(d, "assemblies"))
      {
        var a = LayeredAssembly.FromDictionary(ad);
        if (a != null) c.Assemblies.Add(a);
      }
      foreach (var ud in Ark.List(d, "openingUnits"))
      {
        var unit = OpeningUnit.FromDictionary(ud);
        if (unit != null) c.OpeningUnits.Add(unit);
      }

      c.SyncLayerNames();
      return c;
    }

    /// <summary>Default location of the shared office library.</summary>
    public static string DefaultLibraryPath
    {
      get
      {
        var dir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "Stratum");
        return Path.Combine(dir, "StratumLibrary.3dmlib");
      }
    }

    public bool SaveToFile(string path, out string error)
    {
      error = null;
      try
      {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using (var file = new BinaryArchiveFile(path, BinaryArchiveMode.Write))
        {
          if (!file.Open()) { error = "Could not open " + path + " for writing."; return false; }
          var writer = file.Writer;
          writer.Write3dmChunkVersion(1, 0);
          writer.WriteDictionary(ToDictionary());
          file.Close();
        }
        return true;
      }
      catch (Exception ex)
      {
        error = ex.Message;
        return false;
      }
    }

    public static AssemblyCatalog LoadFromFile(string path, out string error)
    {
      error = null;
      try
      {
        if (!File.Exists(path)) { error = "File not found: " + path; return null; }
        using (var file = new BinaryArchiveFile(path, BinaryArchiveMode.Read))
        {
          if (!file.Open()) { error = "Could not open " + path + " for reading."; return null; }
          var reader = file.Reader;
          reader.Read3dmChunkVersion(out int major, out int minor);
          if (major != 1) { error = "Unsupported library version " + major + "." + minor; return null; }
          var dict = reader.ReadDictionary();
          var catalog = FromDictionary(dict);
          file.Close();
          return catalog;
        }
      }
      catch (Exception ex)
      {
        error = ex.Message;
        return null;
      }
    }
  }
}
