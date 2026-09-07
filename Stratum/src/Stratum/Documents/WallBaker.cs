using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Stratum.Core;
using Stratum.Modeling;

namespace Stratum.Documents
{
  /// <summary>
  /// The only place in Stratum that writes geometry into a Rhino document.
  ///
  /// A wall's layers are baked as separate closed polysurfaces that all belong to
  /// one Rhino group. That single decision gives the selection behaviour a
  /// modeller expects, using Rhino's own rules rather than a bolted-on one:
  ///   * click a wall           -> the whole wall system is selected (the group);
  ///   * Ctrl+Shift+click       -> one individual layer inside the wall;
  ///   * Alt while dragging     -> Rhino's own sub-object drag.
  /// Nothing has to be re-taught.
  /// </summary>
  public static class WallBaker
  {
    /// <summary>Rebuilds every wall in the model. Used after an assembly edit,
    /// a unit change, or the BimRebuild command.</summary>
    public static int RebuildAll(RhinoDoc doc, BimModel model, out List<string> warnings)
    {
      warnings = new List<string>();
      if (doc == null || model == null) return 0;

      var joints = WallJoiner.Solve(doc, model);
      int count = 0;

      using (StratumDoc.Suspend())
      {
        foreach (var wall in model.Walls.ToList())
        {
          WallJoint[] wallJoints;
          if (!joints.TryGetValue(wall.Id, out wallJoints))
            wallJoints = new[] { WallJoint.None, WallJoint.None };

          if (RebuildCore(doc, model, wall, wallJoints[0], wallJoints[1], warnings)) count++;
        }
      }

      doc.Views.Redraw();
      return count;
    }

    /// <summary>Rebuilds one wall and everything that mitres into it.</summary>
    public static bool Rebuild(RhinoDoc doc, BimModel model, WallDefinition wall, out List<string> warnings)
    {
      warnings = new List<string>();
      if (doc == null || model == null || wall == null) return false;

      var joints = WallJoiner.Solve(doc, model);

      // Neighbours share the mitre, so they have to be rebuilt with it.
      var affected = new List<WallDefinition> { wall };
      affected.AddRange(Neighbours(doc, model, wall));

      bool ok = true;
      using (StratumDoc.Suspend())
      {
        foreach (var w in affected.Distinct())
        {
          WallJoint[] j;
          if (!joints.TryGetValue(w.Id, out j)) j = new[] { WallJoint.None, WallJoint.None };
          ok &= RebuildCore(doc, model, w, j[0], j[1], warnings);
        }
      }

      doc.Views.Redraw();
      return ok;
    }

    public static void RebuildMany(RhinoDoc doc, BimModel model, IEnumerable<WallDefinition> walls,
                                   out List<string> warnings)
    {
      warnings = new List<string>();
      if (doc == null || model == null || walls == null) return;

      var joints = WallJoiner.Solve(doc, model);
      var set = new HashSet<Guid>();
      var queue = new List<WallDefinition>();

      foreach (var w in walls)
      {
        if (w == null || !set.Add(w.Id)) continue;
        queue.Add(w);
        foreach (var n in Neighbours(doc, model, w))
          if (set.Add(n.Id)) queue.Add(n);
      }

      using (StratumDoc.Suspend())
      {
        foreach (var w in queue)
        {
          WallJoint[] j;
          if (!joints.TryGetValue(w.Id, out j)) j = new[] { WallJoint.None, WallJoint.None };
          RebuildCore(doc, model, w, j[0], j[1], warnings);
        }
      }

      doc.Views.Redraw();
    }

    static IEnumerable<WallDefinition> Neighbours(RhinoDoc doc, BimModel model, WallDefinition wall)
    {
      if (wall?.Baseline == null) yield break;

      double snap = Math.Max(doc.ModelAbsoluteTolerance * 10.0, 0.5 * Units.InchToModel(doc));
      var ends = new[] { wall.Baseline.PointAtStart, wall.Baseline.PointAtEnd };

      foreach (var other in model.Walls)
      {
        if (other.Id == wall.Id || other.Baseline == null) continue;
        var otherEnds = new[] { other.Baseline.PointAtStart, other.Baseline.PointAtEnd };
        bool touches = ends.Any(a => otherEnds.Any(b =>
          Math.Abs(a.X - b.X) < snap && Math.Abs(a.Y - b.Y) < snap && Math.Abs(a.Z - b.Z) < snap));
        if (touches) yield return other;
      }
    }

    // ------------------------------------------------------------------------

    static bool RebuildCore(RhinoDoc doc, BimModel model, WallDefinition wall,
                            WallJoint startJoint, WallJoint endJoint, List<string> warnings)
    {
      var build = WallBuilder.Build(doc, model, wall, startJoint, endJoint);
      warnings.AddRange(build.Warnings);

      if (!build.Success)
      {
        EraseGeometry(doc, wall);
        return false;
      }

      var assembly = model.AssemblyOf(wall);
      bool wasSelected = wall.LayerObjectIds
        .Select(id => doc.Objects.FindId(id))
        .Any(o => o != null && o.IsSelected(false) > 0);

      EraseGeometry(doc, wall);

      int groupIndex = EnsureGroup(doc, wall);
      var newIds = new List<Guid>();

      foreach (var layerSolid in build.Layers)
      {
        var attributes = BuildAttributes(doc, model, wall, assembly, layerSolid, groupIndex);
        var id = doc.Objects.AddBrep(layerSolid.Brep, attributes);
        if (id != Guid.Empty) newIds.Add(id);
      }

      wall.LayerObjectIds = newIds;
      wall.GroupIndex = groupIndex;

      if (wasSelected)
        foreach (var id in newIds) doc.Objects.Select(id, true, false);

      return newIds.Count > 0;
    }

    /// <summary>Deletes the geometry of a wall but keeps its parametric record.</summary>
    public static void EraseGeometry(RhinoDoc doc, WallDefinition wall)
    {
      if (doc == null || wall == null) return;
      using (StratumDoc.Suspend())
      {
        foreach (var id in wall.LayerObjectIds.ToList())
        {
          var obj = doc.Objects.FindId(id);
          if (obj != null) doc.Objects.Delete(obj, true);
        }
      }
      wall.LayerObjectIds.Clear();
    }

    /// <summary>Deletes a wall completely - geometry and record.</summary>
    public static void DeleteWall(RhinoDoc doc, BimModel model, WallDefinition wall)
    {
      if (model == null || wall == null) return;
      EraseGeometry(doc, wall);
      model.Walls.Remove(wall);
      StratumDoc.Recycle(wall);
    }

    /// <summary>Finds every stray solid that claims to belong to a wall but is not
    /// in that wall's current object list - the debris left behind by Copy/Paste,
    /// Explode or an interrupted command.</summary>
    public static List<RhinoObject> FindOrphans(RhinoDoc doc, BimModel model)
    {
      var orphans = new List<RhinoObject>();
      if (doc == null || model == null) return orphans;

      var known = new HashSet<Guid>(model.Walls.SelectMany(w => w.LayerObjectIds));

      foreach (var obj in doc.Objects)
      {
        if (obj == null || obj.IsDeleted) continue;
        var wallText = obj.Attributes.GetUserString(DocKeys.Wall);
        if (string.IsNullOrEmpty(wallText)) continue;
        if (!known.Contains(obj.Id)) orphans.Add(obj);
      }
      return orphans;
    }

    // ------------------------------------------------------------------------

    static int EnsureGroup(RhinoDoc doc, WallDefinition wall)
    {
      if (wall.GroupIndex >= 0 && doc.Groups.FindIndex(wall.GroupIndex) != null)
        return wall.GroupIndex;

      return doc.Groups.Add(wall.GroupName);
    }

    static ObjectAttributes BuildAttributes(RhinoDoc doc, BimModel model, WallDefinition wall,
                                            LayeredAssembly assembly, WallLayerSolid layerSolid,
                                            int groupIndex)
    {
      var attributes = new ObjectAttributes();
      var layer = layerSolid.Layer;
      var product = layerSolid.Product;

      string layerLabel = string.IsNullOrWhiteSpace(layer.ProductName)
        ? layer.Function.ToString()
        : layer.ProductName;

      attributes.Name = string.Format(CultureInfo.InvariantCulture, "{0} · {1:00} {2}",
                                      assembly?.Code ?? "W?", layerSolid.LayerIndex + 1, layerLabel);

      attributes.LayerIndex = EnsureLayer(doc, assembly, layerSolid, product);

      if (product != null)
      {
        attributes.ObjectColor = product.Color;
        attributes.ColorSource = ObjectColorSource.ColorFromObject;

        int materialIndex = EnsureMaterial(doc, product);
        if (materialIndex >= 0)
        {
          attributes.MaterialIndex = materialIndex;
          attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
        }
      }

      if (groupIndex >= 0) attributes.AddToGroup(groupIndex);

      // ---- the data that makes this a BIM object, not just a solid ----------
      attributes.SetUserString(DocKeys.Wall, wall.Id.ToString());
      attributes.SetUserString(DocKeys.WallName, string.IsNullOrEmpty(wall.Name) ? wall.GroupName : wall.Name);
      attributes.SetUserString(DocKeys.LayerIndex, layerSolid.LayerIndex.ToString(CultureInfo.InvariantCulture));
      attributes.SetUserString(DocKeys.LayerFunction, layer.Function.ToString());
      attributes.SetUserString(DocKeys.Side, layer.Side.ToString());
      attributes.SetUserString(DocKeys.ThicknessIn, layer.ThicknessIn.ToString("0.####", CultureInfo.InvariantCulture));

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

    /// <summary>Layer path: Stratum::Walls::&lt;assembly code&gt;::&lt;nn product&gt;.
    /// One Rhino layer per material means section hatching, print widths and
    /// visibility are all controllable per material, per wall type.</summary>
    static int EnsureLayer(RhinoDoc doc, LayeredAssembly assembly, WallLayerSolid layerSolid,
                           MaterialProduct product)
    {
      string code = Sanitize(assembly?.Code ?? "Unassigned");
      string leaf = Sanitize(string.Format(CultureInfo.InvariantCulture, "{0:00} {1}",
                                           layerSolid.LayerIndex + 1,
                                           product?.Name ?? layerSolid.Layer.Function.ToString()));

      int root = EnsureLayerNamed(doc, DocKeys.RootLayer, -1, System.Drawing.Color.DimGray);
      int walls = EnsureLayerNamed(doc, DocKeys.WallsLayer, root, System.Drawing.Color.DimGray);
      int type = EnsureLayerNamed(doc, code, walls, System.Drawing.Color.Gray);
      return EnsureLayerNamed(doc, leaf, type, product?.Color ?? System.Drawing.Color.Gray);
    }

    static int EnsureLayerNamed(RhinoDoc doc, string name, int parentIndex, System.Drawing.Color color)
    {
      Guid parentId = Guid.Empty;
      if (parentIndex >= 0)
      {
        var parent = doc.Layers[parentIndex];
        if (parent != null) parentId = parent.Id;
      }

      foreach (var existing in doc.Layers)
      {
        if (existing == null || existing.IsDeleted) continue;
        if (existing.ParentLayerId != parentId) continue;
        if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
          return existing.Index;
      }

      var layer = new Layer { Name = name, Color = color };
      if (parentId != Guid.Empty) layer.ParentLayerId = parentId;
      return doc.Layers.Add(layer);
    }

    static string Sanitize(string name)
    {
      if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
      var cleaned = name.Replace("::", "-").Replace(":", "-").Trim();
      return cleaned.Length > 60 ? cleaned.Substring(0, 60) : cleaned;
    }

    static int EnsureMaterial(RhinoDoc doc, MaterialProduct product)
    {
      try
      {
        string name = "Stratum · " + product.Name;

        for (int i = 0; i < doc.Materials.Count; i++)
        {
          var existing = doc.Materials[i];
          if (existing == null || existing.IsDeleted) continue;
          if (string.Equals(existing.Name, name, StringComparison.Ordinal)) return i;
        }

        var material = new Material
        {
          Name = name,
          DiffuseColor = product.Color,
          Reflectivity = 0.0,
          Transparency = product.Category == "Air Gap" ? 0.75 : 0.0
        };
        return doc.Materials.Add(material);
      }
      catch
      {
        return -1;   // materials are a nicety; never let them stop a rebuild
      }
    }
  }
}
