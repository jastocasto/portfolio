using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Stratum.Core;
using Stratum.Modeling;

namespace Stratum.Documents
{
  /// <summary>
  /// Writes floors and roofs into the document, the same way <see cref="WallBaker"/>
  /// writes walls: one solid per material layer, all of them in one group so the
  /// element selects and moves as a unit, filed onto a layer per material, stamped
  /// with the BIM data as 3dm user text, and carrying its own section style so a
  /// clipping plane cuts it correctly.
  /// </summary>
  public static class SlabBaker
  {
    public static int RebuildAll(RhinoDoc doc, BimModel model, out List<string> warnings)
    {
      warnings = new List<string>();
      if (doc == null || model == null) return 0;

      int count = 0;
      using (StratumDoc.Suspend())
      {
        foreach (var slab in model.Slabs.ToList())
          if (RebuildSlab(doc, model, slab, warnings)) count++;

        foreach (var roof in model.Roofs.ToList())
          if (RebuildRoof(doc, model, roof, warnings)) count++;
      }

      doc.Views.Redraw();
      return count;
    }

    public static bool RebuildSlab(RhinoDoc doc, BimModel model, SlabDefinition slab,
                                   List<string> warnings)
    {
      var build = SlabBuilder.BuildSlab(doc, model, slab);
      return Bake(doc, model, slab, build, DocKeys.FloorsLayer, "Floor", warnings);
    }

    public static bool RebuildRoof(RhinoDoc doc, BimModel model, RoofDefinition roof,
                                   List<string> warnings)
    {
      var build = SlabBuilder.BuildRoof(doc, model, roof);
      return Bake(doc, model, roof, build, DocKeys.RoofsLayer, "Roof", warnings);
    }

    // ------------------------------------------------------------------------

    static bool Bake(RhinoDoc doc, BimModel model, LayeredElement element,
                     SlabBuilder.Result build, string folder, string kind,
                     List<string> warnings)
    {
      warnings.AddRange(build.Warnings);

      var assembly = model.Catalog.FindAssembly(element.AssemblyId);

      bool wasSelected = element.LayerObjectIds
        .Select(id => doc.Objects.FindId(id))
        .Any(o => o != null && o.IsSelected(false) > 0);

      Erase(doc, element);

      if (!build.Success) return false;

      int groupIndex = WallBaker.EnsureGroupNamed(doc, element.GroupIndex, element.GroupName);
      var newIds = new List<Guid>();

      using (StratumDoc.Suspend())
      {
        foreach (var layerSolid in build.Layers)
        {
          var attributes = BuildAttributes(doc, element, assembly, layerSolid,
                                           folder, kind, groupIndex);
          var id = doc.Objects.AddBrep(layerSolid.Brep, attributes);
          if (id != Guid.Empty) newIds.Add(id);
        }
      }

      element.LayerObjectIds = newIds;
      element.GroupIndex = groupIndex;

      if (wasSelected)
        foreach (var id in newIds) doc.Objects.Select(id, true, false);

      return newIds.Count > 0;
    }

    public static void Erase(RhinoDoc doc, LayeredElement element)
    {
      if (doc == null || element == null) return;

      using (StratumDoc.Suspend())
      {
        foreach (var id in element.LayerObjectIds.ToList())
        {
          var obj = doc.Objects.FindId(id);
          if (obj != null) doc.Objects.Delete(obj, true);
        }
      }
      element.LayerObjectIds.Clear();
    }

    public static void Delete(RhinoDoc doc, BimModel model, LayeredElement element)
    {
      if (model == null || element == null) return;

      Erase(doc, element);

      var slab = element as SlabDefinition;
      if (slab != null) { model.Slabs.Remove(slab); return; }

      var roof = element as RoofDefinition;
      if (roof != null) model.Roofs.Remove(roof);
    }

    static ObjectAttributes BuildAttributes(RhinoDoc doc, LayeredElement element,
                                            LayeredAssembly assembly,
                                            SlabBuilder.LayerSolid layerSolid,
                                            string folder, string kind, int groupIndex)
    {
      var attributes = new ObjectAttributes();
      var layer = layerSolid.Layer;
      var product = layerSolid.Product;

      string label = string.IsNullOrWhiteSpace(layer.ProductName)
        ? layer.Function.ToString()
        : layer.ProductName;

      attributes.Name = string.Format(CultureInfo.InvariantCulture, "{0} · {1:00} {2}",
                                      assembly?.Code ?? "?", layerSolid.LayerIndex + 1, label);

      attributes.LayerIndex = WallBaker.EnsureElementLayer(doc, folder, assembly,
                                                           layerSolid.LayerIndex,
                                                           layer, product, label);

      if (product != null)
      {
        attributes.ObjectColor = product.Color;
        attributes.ColorSource = ObjectColorSource.ColorFromObject;

        int materialIndex = WallBaker.EnsureMaterial(doc, product);
        if (materialIndex >= 0)
        {
          attributes.MaterialIndex = materialIndex;
          attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
        }

        var sectionStyle = SectionPatterns.BuildStyle(doc, product, layer.IsCore);
        if (sectionStyle != null)
        {
          attributes.SetCustomSectionStyle(sectionStyle);
          attributes.SectionAttributesSource = ObjectSectionAttributesSource.FromObject;
        }
      }

      if (groupIndex >= 0) attributes.AddToGroup(groupIndex);

      attributes.SetUserString(DocKeys.Element, element.Id.ToString());
      attributes.SetUserString(DocKeys.ElementKind, kind);
      attributes.SetUserString(DocKeys.WallName, element.DisplayName);
      attributes.SetUserString(DocKeys.LayerIndex,
        layerSolid.LayerIndex.ToString(CultureInfo.InvariantCulture));
      attributes.SetUserString(DocKeys.LayerFunction, layer.Function.ToString());
      attributes.SetUserString(DocKeys.ThicknessIn,
        layer.ThicknessIn.ToString("0.####", CultureInfo.InvariantCulture));

      if (assembly != null)
      {
        attributes.SetUserString(DocKeys.Assembly, assembly.Id.ToString());
        attributes.SetUserString(DocKeys.AssemblyCode, assembly.Code);
      }

      if (product != null)
      {
        attributes.SetUserString(DocKeys.Product, product.Id.ToString());
        attributes.SetUserString(DocKeys.ProductName, product.Name);
        attributes.SetUserString(DocKeys.Manufacturer, product.Manufacturer);
        attributes.SetUserString(DocKeys.Sku, product.Sku);
        attributes.SetUserString(DocKeys.RValue,
          product.RValueAt(layer.ThicknessIn).ToString("0.##", CultureInfo.InvariantCulture));
        attributes.SetUserString(DocKeys.CostPerSqFt,
          (product.CostPerSqFt * (1.0 + product.WasteFactor)).ToString("0.##", CultureInfo.InvariantCulture));
      }

      return attributes;
    }

    /// <summary>The floor or roof a baked object belongs to, if any.</summary>
    public static LayeredElement ElementOf(BimModel model, RhinoObject obj)
    {
      if (model == null || obj == null) return null;

      var text = obj.Attributes.GetUserString(DocKeys.Element);
      Guid id;
      if (string.IsNullOrEmpty(text) || !Guid.TryParse(text, out id)) return null;

      return (LayeredElement)model.FindSlab(id) ?? model.FindRoof(id);
    }
  }
}
