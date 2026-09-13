using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Modeling;

namespace Stratum.Commands
{
  /// <summary>Shared plumbing for the Stratum commands.</summary>
  public static class CommandUtil
  {
    /// <summary>Makes sure the document has something sensible to draw with.</summary>
    public static BimModel Prepare(RhinoDoc doc)
    {
      var model = StratumDoc.Get(doc);

      if (model.Catalog.Assemblies.Count == 0)
        model.Catalog.Merge(CatalogDefaults.Create());

      if (model.Catalog.FindAssembly(model.ActiveAssemblyId) == null)
        model.ActiveAssemblyId = model.Catalog.Assemblies.First().Id;

      model.EnsureLevels();

      if (model.ActiveHeight <= RhinoMath.ZeroTolerance)
        model.ActiveHeight = 8.0 * Units.FeetToModel(doc);   // 8'-0" is the default storey

      return model;
    }

    /// <summary>Justification option names in the words this kind of element uses,
    /// so a wall offers ExteriorFace and a floor would offer TopFace.</summary>
    public static string[] JustificationNames(LayeredAssembly assembly)
      => AssemblyNaming.OptionNames(assembly?.Kind ?? AssemblyKind.Wall);

    public static string[] AssemblyCodes(BimModel model)
      => model.Catalog.Assemblies.Select(a => Sanitize(a.Code)).ToArray();

    /// <summary>Command-line option values may not contain spaces or punctuation.</summary>
    public static string Sanitize(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return "Item";
      var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
      return string.IsNullOrEmpty(cleaned) ? "Item" : cleaned;
    }

    // ---- preview -----------------------------------------------------------

    static readonly Dictionary<int, DisplayMaterial> MaterialCache = new Dictionary<int, DisplayMaterial>();

    static DisplayMaterial MaterialFor(MaterialProduct product)
    {
      var color = product?.Color ?? Color.Gray;
      double transparency = product != null && product.Category == "Air Gap" ? 0.7 : 0.15;
      int key = color.ToArgb() ^ (int)(transparency * 1000);

      DisplayMaterial material;
      if (MaterialCache.TryGetValue(key, out material)) return material;

      material = new DisplayMaterial(color, transparency);
      MaterialCache[key] = material;
      return material;
    }

    /// <summary>Draws a built wall into a viewport - used by every command that
    /// shows a wall before it is committed. This is the same geometry that gets
    /// baked, not a stand-in, so what is dragged is what is built.</summary>
    public static void DrawPreview(DisplayPipeline display, WallBuildResult build,
                                   Curve baseline = null, bool shaded = true)
    {
      if (display == null || build == null) return;

      foreach (var layer in build.Layers)
      {
        foreach (var solid in layer.Solids)
        {
          if (solid == null) continue;
          if (shaded) display.DrawBrepShaded(solid, MaterialFor(layer.Product));
          display.DrawBrepWires(solid, ColorFor(layer.Product), 0);
        }
      }

      if (baseline != null)
        display.DrawCurve(baseline, Color.FromArgb(255, 60, 130, 220), 2);
    }

    static Color ColorFor(MaterialProduct product)
    {
      var c = product?.Color ?? Color.Gray;
      return Color.FromArgb(255, (int)(c.R * 0.55), (int)(c.G * 0.55), (int)(c.B * 0.55));
    }

    /// <summary>Builds a throwaway wall for previewing, without touching the document.</summary>
    public static WallBuildResult PreviewWall(RhinoDoc doc, BimModel model, Curve baseline,
                                              Guid assemblyId, double baseElevation, double height,
                                              AssemblyJustification justification, bool flipped)
    {
      if (baseline == null || baseline.GetLength() <= doc.ModelAbsoluteTolerance)
        return new WallBuildResult();

      var temp = new WallDefinition
      {
        AssemblyId = assemblyId,
        Baseline = baseline,
        BaseElevation = baseElevation,
        Height = height,
        Justification = justification,
        Flipped = flipped
      };

      return WallBuilder.Build(doc, model, temp, WallJunctions.None, includeOpenings: false);
    }

    /// <summary>Prints the warnings a build produced, without flooding the command line.</summary>
    public static void ReportWarnings(IEnumerable<string> warnings)
    {
      if (warnings == null) return;
      foreach (var warning in warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().Take(6))
        RhinoApp.WriteLine("Stratum: " + warning);
    }

    /// <summary>Human-readable summary used in the command line and the panel.</summary>
    public static string Describe(RhinoDoc doc, BimModel model, LayeredAssembly assembly)
    {
      if (assembly == null) return "no assembly";
      return string.Format("{0} · {1} · {2} · R-{3:0.0} · ${4:0.00}/sf",
        assembly.Code,
        assembly.Name,
        Units.FormatInches(doc, assembly.TotalThicknessIn),
        assembly.EffectiveRValue(model.Catalog),
        assembly.CostPerSqFt(model.Catalog));
    }
  }
}
