using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Documents
{
  /// <summary>
  /// Installs the construction hatch patterns Stratum draws sections with, and
  /// builds the <see cref="SectionStyle"/> that each layer solid carries.
  ///
  /// This is what turns a clipping plane from a picture of white boxes into a
  /// readable construction section. Rhino 8 lets an object carry its own section
  /// style - hatch, poche fill and cut-line weight - so once a wall is baked, any
  /// clipping plane anywhere in the model produces a correctly poche'd cut with no
  /// further work and nothing to keep in sync.
  ///
  /// Patterns are authored at one unit per inch and scaled to the document's units
  /// when the style is built, so the same catalog reads correctly in an imperial or
  /// a metric file.
  ///
  /// Honest limitation: Rhino hatch patterns are families of straight lines, and
  /// RhinoCommon exposes no way to set the dash array on one programmatically. The
  /// conventional squiggle for batt insulation and the stipple-and-triangle for
  /// concrete therefore cannot be drawn exactly; both are approximated by line
  /// families at the right density and angle, which is what most offices use at
  /// anything below full detail scale anyway.
  /// </summary>
  public static class SectionPatterns
  {
    /// <summary>angle in degrees, spacing in inches, shift along the line in inches.</summary>
    struct LineFamily
    {
      public double AngleDegrees, SpacingInches, ShiftInches;
      public LineFamily(double angle, double spacing, double shift = 0.0)
      {
        AngleDegrees = angle; SpacingInches = spacing; ShiftInches = shift;
      }
    }

    static readonly Dictionary<string, LineFamily[]> Definitions =
      new Dictionary<string, LineFamily[]>(StringComparer.OrdinalIgnoreCase)
      {
        // Crossed at 45 degrees, coarse - reads as concrete at section scale.
        { SectionPatternNames.Concrete,        new[] { new LineFamily(45, 0.140), new LineFamily(-45, 0.140) } },
        // Single 45 degree family, the long-standing convention for CMU.
        { SectionPatternNames.Masonry,         new[] { new LineFamily(45, 0.190) } },
        { SectionPatternNames.Brick,           new[] { new LineFamily(45, 0.070) } },
        { SectionPatternNames.Earth,           new[] { new LineFamily(45, 0.095) } },
        // Tight cross-hatch for rigid board.
        { SectionPatternNames.RigidInsulation, new[] { new LineFamily(45, 0.065), new LineFamily(-45, 0.065) } },
        // Dense shallow diagonal standing in for the batt squiggle.
        { SectionPatternNames.BattInsulation,  new[] { new LineFamily(30, 0.050) } },
        // Shallow fine lines, reading as grain.
        { SectionPatternNames.Wood,            new[] { new LineFamily(15, 0.060) } },
        // Near-solid cross-hatch; steel is usually blacked out at this scale.
        { SectionPatternNames.Steel,           new[] { new LineFamily(45, 0.028), new LineFamily(-45, 0.028) } },
        // Very light, so gypsum reads as a thin finish rather than a material.
        { SectionPatternNames.Gypsum,          new[] { new LineFamily(45, 0.250) } },
      };

    /// <summary>
    /// Makes sure a pattern exists in the document and returns its index.
    /// Returns -1 for "no hatch", which the section style reads as poche only.
    /// </summary>
    public static int EnsurePattern(RhinoDoc doc, string name)
    {
      if (doc == null || string.IsNullOrWhiteSpace(name)) return -1;

      var existing = doc.HatchPatterns.FindName(name);
      if (existing != null) return existing.Index;

      try
      {
        var pattern = new HatchPattern { Name = name };

        if (string.Equals(name, SectionPatternNames.Solid, StringComparison.OrdinalIgnoreCase))
        {
          pattern.FillType = HatchPatternFillType.Solid;
          pattern.Description = "Stratum: solid cut fill";
          return doc.HatchPatterns.Add(pattern);
        }

        LineFamily[] families;
        if (!Definitions.TryGetValue(name, out families)) return -1;

        pattern.FillType = HatchPatternFillType.Lines;
        pattern.Description = "Stratum construction pattern";

        foreach (var family in families)
        {
          double radians = RhinoMath.ToRadians(family.AngleDegrees);
          var line = new HatchLine
          {
            Angle = radians,
            BasePoint = Point2d.Origin,
            // In a hatch line's own frame, X shifts successive copies along the
            // line and Y is the gap between them.
            Offset = new Vector2d(family.ShiftInches, family.SpacingInches)
          };
          pattern.AddHatchLine(line);
        }

        return doc.HatchPatterns.Add(pattern);
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Stratum: could not create hatch pattern '" + name + "' - " + ex.Message);
        return -1;
      }
    }

    /// <summary>Installs every Stratum pattern the catalog refers to, once.</summary>
    public static void EnsureAll(RhinoDoc doc, AssemblyCatalog catalog)
    {
      if (doc == null || catalog == null) return;

      var wanted = catalog.Products
        .Select(p => p.SectionHatchPattern)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase);

      foreach (var name in wanted) EnsurePattern(doc, name);
    }

    /// <summary>
    /// Builds the section style for one layer. Returns null when the product is
    /// unknown, in which case the layer just uses Rhino's own defaults.
    /// </summary>
    public static SectionStyle BuildStyle(RhinoDoc doc, MaterialProduct product, bool isCore)
    {
      if (doc == null || product == null) return null;

      try
      {
        var style = new SectionStyle();

        int hatchIndex = EnsurePattern(doc, product.SectionHatchPattern);
        if (hatchIndex >= 0)
        {
          style.HatchIndex = hatchIndex;
          // Patterns are authored per inch; scale into the document's units so an
          // imperial and a metric file both cut at the same physical spacing.
          style.HatchScale = Math.Max(0.01, product.SectionHatchScale) * Units.InchToModel(doc);
          style.HatchRotationRadians = RhinoMath.ToRadians(product.SectionHatchRotationDegrees);
          style.HatchPatternColor = product.SectionHatchColor;
          style.HatchPatternPrintColor = product.SectionHatchColor;
        }

        // The poche. Without it a section is white boxes with hatching floating in
        // them, which is exactly what makes a Rhino section unreadable by default.
        style.BackgroundFillMode = SectionBackgroundFillMode.SolidColor;
        style.BackgroundFillColor = product.SectionFillColor;
        style.BackgroundFillPrintColor = product.SectionFillColor;

        // The cut line. Structure reads first on a construction section, so the
        // core gets a heavier boundary than the layers wrapped around it.
        style.BoundaryVisible = true;
        style.BoundaryColor = product.SectionHatchColor;
        style.BoundaryPrintColor = System.Drawing.Color.Black;
        style.BoundaryWidthScale = Math.Max(0.1, product.SectionLineWeightScale) * (isCore ? 2.0 : 1.0);

        return style;
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Stratum: could not build a section style for '" +
                           product.Name + "' - " + ex.Message);
        return null;
      }
    }
  }
}
