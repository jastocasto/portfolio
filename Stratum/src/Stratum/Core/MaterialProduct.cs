using System;
using System.Drawing;
using Rhino.Collections;

namespace Stratum.Core
{
  /// <summary>
  /// A real, orderable product - not a generic "material". A layer in an
  /// assembly points at one of these, so changing "1/2 in plywood" to
  /// "3/4 in plywood" simultaneously updates the wall thickness, the
  /// R-value, the mass and the cost that feed the schedules.
  ///
  /// All dimensions are stored in INCHES. Rhino model units are applied at
  /// geometry time (see <see cref="Units"/>) so a catalog is portable between
  /// documents regardless of their unit system.
  /// </summary>
  public class MaterialProduct
  {
    public Guid Id = Guid.NewGuid();

    /// <summary>Catalog grouping, e.g. "Sheathing", "Insulation", "Finish".</summary>
    public string Category = "General";
    public string Name = "New product";
    public string Manufacturer = "";
    public string Sku = "";

    /// <summary>Actual (not nominal) thickness in inches. 0 for products whose
    /// thickness is set by the assembly, e.g. a poured or sprayed layer.</summary>
    public double ThicknessIn = 0.5;

    /// <summary>Thermal resistance per inch, h·ft²·°F/Btu. Ignored when
    /// <see cref="RValueTotal"/> is greater than zero.</summary>
    public double RPerInch = 0.0;

    /// <summary>Fixed thermal resistance for the product as supplied. Use for
    /// products whose R does not scale linearly with thickness (air films, gaps).</summary>
    public double RValueTotal = 0.0;

    /// <summary>Installed cost per square foot of wall face, in dollars.</summary>
    public double CostPerSqFt = 0.0;

    /// <summary>Waste allowance applied to the material cost, e.g. 0.10 for 10%.</summary>
    public double WasteFactor = 0.10;

    /// <summary>Density, pounds per cubic foot - drives dead load takeoffs.</summary>
    public double DensityPcf = 0.0;

    /// <summary>Water vapour permeance (US perms) of the product as supplied.
    /// Below 1.0 is a vapour retarder; below 0.1 is a vapour barrier.</summary>
    public double PermRating = 0.0;

    public bool Combustible = false;

    /// <summary>May be used as the structural core of an assembly.</summary>
    public bool StructuralCapable = false;

    /// <summary>Colour used for the layer solid in the model and in sections.</summary>
    public int ColorArgb = unchecked((int)0xFFB0B0B0);

    // ---- how this product reads on a section cut -------------------------
    //
    // A construction document is not a shaded view. When a clipping plane cuts
    // the model, each material has to read as itself: concrete hatched as
    // concrete, insulation as insulation, the structural core in a heavy line.
    // These drive the Rhino 8 section style attached to every layer solid.

    /// <summary>Name of the hatch pattern used where this product is cut.
    /// See SectionPatterns for the set Stratum installs. Empty means no hatch,
    /// just the poche fill.</summary>
    public string SectionHatchPattern = "";

    /// <summary>Multiplier on the hatch spacing. The patterns are authored at
    /// one unit per inch and scaled to the document's units automatically, so
    /// this is a fine adjustment, not a unit conversion.</summary>
    public double SectionHatchScale = 1.0;

    public double SectionHatchRotationDegrees = 0.0;

    /// <summary>Multiplier on the width of the cut boundary. Structural layers
    /// are set heavier so the structure reads first on a section.</summary>
    public double SectionLineWeightScale = 1.0;

    /// <summary>Solid fill behind the hatch. Zero means "derive it from the
    /// product colour", which keeps the section reading like the model without
    /// anyone maintaining a second palette.</summary>
    public int SectionFillColorArgb = 0;

    public string Notes = "";

    /// <summary>The poche colour actually used: the explicit override if set,
    /// otherwise a lightened version of the product colour.</summary>
    public Color SectionFillColor
    {
      get
      {
        if (SectionFillColorArgb != 0) return Color.FromArgb(SectionFillColorArgb);
        var c = Color;
        return Color.FromArgb(255,
          c.R + (255 - c.R) * 55 / 100,
          c.G + (255 - c.G) * 55 / 100,
          c.B + (255 - c.B) * 55 / 100);
      }
    }

    /// <summary>Hatch line colour: a darkened version of the product colour, so
    /// the hatch always sits legibly on its own poche.</summary>
    public Color SectionHatchColor
    {
      get
      {
        var c = Color;
        return Color.FromArgb(255, c.R * 45 / 100, c.G * 45 / 100, c.B * 45 / 100);
      }
    }

    public Color Color
    {
      get { return Color.FromArgb(ColorArgb); }
      set { ColorArgb = value.ToArgb(); }
    }

    /// <summary>R-value of this product at a given installed thickness (inches).</summary>
    public double RValueAt(double thicknessInches)
    {
      if (RValueTotal > 0.0)
      {
        // Scale a published fixed R only when the layer is thicker/thinner than
        // the product's own thickness and the product is a bulk insulator.
        if (ThicknessIn > 1e-9 && RPerInch <= 0.0)
          return RValueTotal * (thicknessInches / ThicknessIn);
        return RValueTotal;
      }
      return RPerInch * thicknessInches;
    }

    public string DisplayName
    {
      get
      {
        if (string.IsNullOrWhiteSpace(Manufacturer)) return Name;
        return Name + "  (" + Manufacturer + ")";
      }
    }

    public MaterialProduct Duplicate(bool newId = true)
    {
      var p = (MaterialProduct)MemberwiseClone();
      if (newId) p.Id = Guid.NewGuid();
      return p;
    }

    // ---- persistence -------------------------------------------------------

    public ArchivableDictionary ToDictionary()
    {
      var d = new ArchivableDictionary();
      Ark.Put(d, "id", Id);
      Ark.Put(d, "category", Category);
      Ark.Put(d, "name", Name);
      Ark.Put(d, "manufacturer", Manufacturer);
      Ark.Put(d, "sku", Sku);
      Ark.Put(d, "thicknessIn", ThicknessIn);
      Ark.Put(d, "rPerInch", RPerInch);
      Ark.Put(d, "rTotal", RValueTotal);
      Ark.Put(d, "costPerSqFt", CostPerSqFt);
      Ark.Put(d, "waste", WasteFactor);
      Ark.Put(d, "densityPcf", DensityPcf);
      Ark.Put(d, "perm", PermRating);
      Ark.Put(d, "combustible", Combustible);
      Ark.Put(d, "structural", StructuralCapable);
      Ark.Put(d, "color", ColorArgb);
      Ark.Put(d, "sectionHatch", SectionHatchPattern);
      Ark.Put(d, "sectionHatchScale", SectionHatchScale);
      Ark.Put(d, "sectionHatchRotation", SectionHatchRotationDegrees);
      Ark.Put(d, "sectionLineWeight", SectionLineWeightScale);
      Ark.Put(d, "sectionFill", SectionFillColorArgb);
      Ark.Put(d, "notes", Notes);
      return d;
    }

    public static MaterialProduct FromDictionary(ArchivableDictionary d)
    {
      if (d == null) return null;
      return new MaterialProduct
      {
        Id = Ark.Id(d, "id"),
        Category = Ark.Str(d, "category", "General"),
        Name = Ark.Str(d, "name", "Product"),
        Manufacturer = Ark.Str(d, "manufacturer"),
        Sku = Ark.Str(d, "sku"),
        ThicknessIn = Ark.Num(d, "thicknessIn", 0.5),
        RPerInch = Ark.Num(d, "rPerInch"),
        RValueTotal = Ark.Num(d, "rTotal"),
        CostPerSqFt = Ark.Num(d, "costPerSqFt"),
        WasteFactor = Ark.Num(d, "waste", 0.10),
        DensityPcf = Ark.Num(d, "densityPcf"),
        PermRating = Ark.Num(d, "perm"),
        Combustible = Ark.Bool(d, "combustible"),
        StructuralCapable = Ark.Bool(d, "structural"),
        ColorArgb = Ark.Int(d, "color", unchecked((int)0xFFB0B0B0)),
        SectionHatchPattern = Ark.Str(d, "sectionHatch"),
        SectionHatchScale = Ark.Num(d, "sectionHatchScale", 1.0),
        SectionHatchRotationDegrees = Ark.Num(d, "sectionHatchRotation"),
        SectionLineWeightScale = Ark.Num(d, "sectionLineWeight", 1.0),
        SectionFillColorArgb = Ark.Int(d, "sectionFill"),
        Notes = Ark.Str(d, "notes")
      };
    }
  }
}
