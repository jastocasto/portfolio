using System;
using System.Collections.Generic;
using System.Drawing;

namespace Stratum.Core
{
  /// <summary>
  /// The catalog Stratum seeds a new document with. Values are typical North
  /// American residential/light-commercial construction: actual (not nominal)
  /// thicknesses, published R-values, and 2024-era installed unit costs.
  ///
  /// Treat the costs as placeholders to be replaced with your own supplier
  /// pricing - the point is that every number here is a real property of a real
  /// product, and every wall in the model is priced from it.
  /// </summary>
  public static class CatalogDefaults
  {
    static MaterialProduct P(string category, string name, double thicknessIn,
                             double rPerInch, double rTotal, double cost,
                             double density, double perm, int argb,
                             bool structural = false, bool combustible = false,
                             string manufacturer = "", string sku = "")
    {
      return new MaterialProduct
      {
        Category = category,
        Name = name,
        Manufacturer = manufacturer,
        Sku = sku,
        ThicknessIn = thicknessIn,
        RPerInch = rPerInch,
        RValueTotal = rTotal,
        CostPerSqFt = cost,
        DensityPcf = density,
        PermRating = perm,
        ColorArgb = argb,
        StructuralCapable = structural,
        Combustible = combustible
      };
    }

    static int C(int r, int g, int b) => Color.FromArgb(255, r, g, b).ToArgb();

    /// <summary>
    /// Chooses how a product reads on a section cut, from what it is made of.
    ///
    /// Applied to every seeded product and to anything the user creates, so a new
    /// catalog entry sections sensibly straight away instead of appearing as a
    /// blank box. Override it per product in the catalog editor when the
    /// convention in your office differs.
    /// </summary>
    public static void ApplySectionDefaults(MaterialProduct p)
    {
      if (p == null) return;

      string n = (p.Name ?? string.Empty).ToLowerInvariant();
      string category = (p.Category ?? string.Empty).ToLowerInvariant();

      // Membranes and cavities are too thin to hatch legibly; they read as a
      // poche line, which is exactly how they are drawn by hand.
      if (category == "membrane" || category == "air gap")
      {
        p.SectionHatchPattern = "";
        p.SectionLineWeightScale = 0.5;
        return;
      }

      if (n.Contains("cmu") || n.Contains("stone")) p.SectionHatchPattern = SectionPatternNames.Masonry;
      else if (n.Contains("brick")) p.SectionHatchPattern = SectionPatternNames.Brick;
      else if (n.Contains("concrete") || n.Contains("stucco") || n.Contains("icf"))
        p.SectionHatchPattern = SectionPatternNames.Concrete;
      else if (n.Contains("steel") || n.Contains("metal") || n.Contains("hat channel"))
        p.SectionHatchPattern = SectionPatternNames.Steel;
      else if (n.Contains("batt")) p.SectionHatchPattern = SectionPatternNames.BattInsulation;
      else if (category == "insulation") p.SectionHatchPattern = SectionPatternNames.RigidInsulation;
      else if (n.Contains("gypsum")) p.SectionHatchPattern = SectionPatternNames.Gypsum;
      else if (n.Contains("plywood") || n.Contains("osb") || n.Contains("wood") ||
               n.Contains("cedar") || n.Contains("furring") || n.Contains("stud"))
        p.SectionHatchPattern = SectionPatternNames.Wood;
      else if (n.Contains("fiber cement")) p.SectionHatchPattern = SectionPatternNames.Solid;
      else p.SectionHatchPattern = "";

      // Structure carries the heavy cut line on a section; BuildStyle doubles it
      // again for whichever layer is the core.
      p.SectionLineWeightScale = category == "structure" ? 1.25 : 1.0;
    }

    public static AssemblyCatalog Create()
    {
      var cat = new AssemblyCatalog();

      // ---- interior finishes ----------------------------------------------
      var gyp12 = P("Finish", "Gypsum board, 1/2\"", 0.5, 0.0, 0.45, 2.10, 40, 25, C(238, 238, 232));
      var gyp58 = P("Finish", "Gypsum board, 5/8\" Type X", 0.625, 0.0, 0.56, 2.35, 43, 20, C(232, 232, 224));
      var gyp58x2 = P("Finish", "Gypsum board, 5/8\" Type X (2 layers)", 1.25, 0.0, 1.12, 4.60, 43, 15, C(226, 226, 218));
      var plyFin = P("Finish", "Plywood panelling, 1/2\"", 0.5, 1.25, 0.0, 4.20, 34, 0.7, C(206, 172, 116), false, true);

      // ---- sheathing -------------------------------------------------------
      var ply12 = P("Sheathing", "Plywood CDX, 1/2\"", 0.5, 1.25, 0.0, 2.40, 34, 0.8, C(214, 178, 120), false, true);
      var ply58 = P("Sheathing", "Plywood CDX, 5/8\"", 0.625, 1.25, 0.0, 2.85, 34, 0.7, C(210, 174, 116), false, true);
      var ply34 = P("Sheathing", "Plywood CDX, 3/4\"", 0.75, 1.25, 0.0, 3.30, 34, 0.6, C(206, 170, 112), false, true);
      var osb716 = P("Sheathing", "OSB, 7/16\"", 0.4375, 1.24, 0.0, 1.85, 40, 1.0, C(198, 160, 104), false, true);
      var osb58 = P("Sheathing", "OSB, 5/8\"", 0.625, 1.24, 0.0, 2.30, 40, 0.8, C(194, 156, 100), false, true);
      var zip = P("Sheathing", "Integrated WRB sheathing, 7/16\"", 0.4375, 1.24, 0.0, 2.95, 40, 12, C(180, 140, 90), false, true, "Huber", "ZIP-7/16");
      var gypSheath = P("Sheathing", "Glass-mat gypsum sheathing, 5/8\"", 0.625, 0.0, 0.56, 2.75, 43, 23, C(226, 220, 196));

      // ---- structure -------------------------------------------------------
      var stud2x4 = P("Structure", "Wood stud 2x4 @ 16\" o.c.", 3.5, 0.0, 0.0, 5.20, 8, 20, C(222, 190, 140), true, true);
      var stud2x6 = P("Structure", "Wood stud 2x6 @ 16\" o.c.", 5.5, 0.0, 0.0, 6.40, 8, 20, C(222, 190, 140), true, true);
      var stud2x8 = P("Structure", "Wood stud 2x8 @ 16\" o.c.", 7.25, 0.0, 0.0, 7.90, 8, 20, C(222, 190, 140), true, true);
      var mtl358 = P("Structure", "Steel stud 3-5/8\" 20ga @ 16\" o.c.", 3.625, 0.0, 0.0, 5.90, 4, 30, C(176, 186, 198), true);
      var mtl6 = P("Structure", "Steel stud 6\" 20ga @ 16\" o.c.", 6.0, 0.0, 0.0, 6.80, 4, 30, C(176, 186, 198), true);
      var cmu6 = P("Structure", "CMU, 6\" normal weight", 5.625, 0.0, 1.04, 12.50, 85, 2.4, C(178, 178, 174), true);
      var cmu8 = P("Structure", "CMU, 8\" normal weight", 7.625, 0.0, 1.11, 14.75, 85, 2.4, C(174, 174, 170), true);
      var conc8 = P("Structure", "Cast-in-place concrete, 8\"", 8.0, 0.08, 0.0, 22.00, 145, 3.2, C(158, 158, 158), true);
      var conc12 = P("Structure", "Cast-in-place concrete, 12\"", 12.0, 0.08, 0.0, 30.00, 145, 2.4, C(152, 152, 152), true);
      var icf = P("Structure", "ICF, 6\" core with 2-1/2\" EPS each face", 11.0, 0.0, 23.0, 26.00, 80, 1.0, C(200, 205, 210), true);

      // ---- insulation ------------------------------------------------------
      var batt13 = P("Insulation", "Fiberglass batt R-13 (3-1/2\")", 3.5, 0.0, 13.0, 1.15, 0.7, 30, C(255, 214, 130));
      var batt15 = P("Insulation", "Fiberglass batt R-15 high density (3-1/2\")", 3.5, 0.0, 15.0, 1.55, 1.0, 30, C(255, 206, 118));
      var batt21 = P("Insulation", "Fiberglass batt R-21 (5-1/2\")", 5.5, 0.0, 21.0, 1.75, 0.9, 30, C(255, 198, 106));
      var mwBatt = P("Insulation", "Mineral wool batt R-23 (5-1/2\")", 5.5, 0.0, 23.0, 2.60, 2.5, 30, C(226, 176, 150));
      var ccSpf = P("Insulation", "Closed-cell spray foam", 1.0, 6.5, 0.0, 2.20, 2.0, 0.8, C(240, 226, 190));
      var polyiso = P("Insulation", "Polyisocyanurate board, foil-faced", 1.0, 6.0, 0.0, 1.45, 2.0, 0.03, C(246, 224, 160));
      var xps = P("Insulation", "XPS rigid board", 1.0, 5.0, 0.0, 1.30, 1.8, 1.1, C(178, 214, 236));
      var eps = P("Insulation", "EPS rigid board Type II", 1.0, 4.2, 0.0, 0.85, 1.5, 3.5, C(226, 236, 240));
      var mwBoard = P("Insulation", "Mineral wool board (continuous)", 1.0, 4.3, 0.0, 2.40, 8.0, 30, C(220, 168, 142));

      // ---- membranes -------------------------------------------------------
      var wrb = P("Membrane", "Mechanically fastened WRB (housewrap)", 0.01, 0.0, 0.0, 0.42, 0, 10, C(120, 190, 140));
      var saWrb = P("Membrane", "Self-adhered vapour-permeable WRB", 0.04, 0.0, 0.0, 1.65, 0, 22, C(96, 172, 122));
      var vapourRet = P("Membrane", "Polyethylene vapour retarder, 6 mil", 0.006, 0.0, 0.0, 0.24, 0, 0.06, C(150, 200, 230));
      var smartVb = P("Membrane", "Variable-permeance smart vapour retarder", 0.012, 0.0, 0.0, 0.65, 0, 1.0, C(140, 190, 226));
      var airBarrier = P("Membrane", "Fluid-applied air barrier", 0.03, 0.0, 0.0, 2.10, 0, 12, C(110, 160, 120));

      // ---- cavities and furring -------------------------------------------
      var gap34 = P("Air Gap", "Ventilated rainscreen cavity, 3/4\"", 0.75, 0.0, 0.0, 0.00, 0, 100, C(214, 226, 236));
      var gap1 = P("Air Gap", "Drained cavity, 1\"", 1.0, 0.0, 0.0, 0.00, 0, 100, C(214, 226, 236));
      var brickCav = P("Air Gap", "Brick veneer cavity, 2\"", 2.0, 0.0, 1.0, 0.00, 0, 100, C(210, 222, 232));
      var furr34 = P("Furring", "Wood furring 1x4 @ 16\" o.c. (3/4\")", 0.75, 0.0, 0.0, 1.35, 3, 30, C(206, 178, 132), false, true);
      var furr15 = P("Furring", "Wood furring 2x2 @ 16\" o.c. (1-1/2\")", 1.5, 0.0, 0.0, 1.85, 3, 30, C(206, 178, 132), false, true);
      var hatChan = P("Furring", "Metal hat channel 7/8\"", 0.875, 0.0, 0.0, 1.55, 1, 30, C(186, 194, 204));

      // ---- cladding --------------------------------------------------------
      var fiberCement = P("Cladding", "Fiber cement lap siding, 8-1/4\" exposure", 0.3125, 0.0, 0.15, 6.40, 90, 5, C(160, 168, 172), false, false, "James Hardie", "HardiePlank");
      var cedar = P("Cladding", "Cedar bevel siding, 3/4\"", 0.75, 1.2, 0.0, 9.80, 23, 5, C(190, 138, 92), false, true);
      var brick = P("Cladding", "Modular brick veneer, 3-5/8\"", 3.625, 0.0, 0.44, 22.50, 120, 1.5, C(158, 88, 68));
      var stucco = P("Cladding", "Three-coat stucco, 7/8\"", 0.875, 0.20, 0.0, 11.00, 116, 8, C(220, 214, 200));
      var metalPanel = P("Cladding", "Standing seam metal panel, 24ga", 0.0625, 0.0, 0.05, 14.00, 490, 0.0, C(120, 128, 136));
      var stoneVen = P("Cladding", "Adhered stone veneer, 1-1/2\"", 1.5, 0.0, 0.20, 26.00, 130, 2.0, C(140, 132, 122));

      cat.Products.AddRange(new[]
      {
        gyp12, gyp58, gyp58x2, plyFin,
        ply12, ply58, ply34, osb716, osb58, zip, gypSheath,
        stud2x4, stud2x6, stud2x8, mtl358, mtl6, cmu6, cmu8, conc8, conc12, icf,
        batt13, batt15, batt21, mwBatt, ccSpf, polyiso, xps, eps, mwBoard,
        wrb, saWrb, vapourRet, smartVb, airBarrier,
        gap34, gap1, brickCav, furr34, furr15, hatChan,
        fiberCement, cedar, brick, stucco, metalPanel, stoneVen
      });

      // ---------------------------------------------------------------------
      //  Wall types. Layers are listed exterior -> interior.
      // ---------------------------------------------------------------------

      cat.Assemblies.Add(Assembly("W1", "2x6 wood frame, 2\" exterior polyiso",
        "Rainscreen fiber cement over continuous exterior insulation. Meets IECC CZ5-6 prescriptive.",
        1.0, 45,
        L(fiberCement, LayerFunction.Cladding, EdgeResolution.Wrap, 0.75),
        L(furr34, LayerFunction.Furring, EdgeResolution.Butt),
        L(wrb, LayerFunction.Membrane, EdgeResolution.Wrap, 0.5),
        L(polyiso, LayerFunction.Insulation, EdgeResolution.Butt, 0, 2.0),
        L(osb716, LayerFunction.Sheathing, EdgeResolution.Butt),
        Core(stud2x6, 5.5, batt21),
        L(smartVb, LayerFunction.Membrane, EdgeResolution.Wrap, 0.5),
        L(gyp12, LayerFunction.Finish, EdgeResolution.Wrap, 1.0)));

      cat.Assemblies.Add(Assembly("W2", "2x4 interior partition, 5/8\" both sides",
        "Non-rated interior partition.", 0.0, 35,
        L(gyp58, LayerFunction.Finish, EdgeResolution.Wrap, 0.75),
        Core(stud2x4, 3.5, batt13),
        L(gyp58, LayerFunction.Finish, EdgeResolution.Wrap, 0.75)));

      cat.Assemblies.Add(Assembly("W3", "1-hour rated steel stud partition",
        "UL U419 equivalent. Two layers 5/8\" Type X each side of 3-5/8\" steel stud.",
        1.0, 50,
        L(gyp58x2, LayerFunction.Finish, EdgeResolution.Wrap, 0.75),
        Core(mtl358, 3.625, batt15),
        L(gyp58x2, LayerFunction.Finish, EdgeResolution.Wrap, 0.75)));

      cat.Assemblies.Add(Assembly("W4", "Brick veneer over 2x6 wood frame",
        "Anchored masonry veneer with drained and ventilated 2\" cavity.",
        1.0, 52,
        L(brick, LayerFunction.Cladding, EdgeResolution.ReturnToFrame, 1.0),
        L(brickCav, LayerFunction.AirGap, EdgeResolution.Butt),
        L(saWrb, LayerFunction.Membrane, EdgeResolution.Wrap, 0.5),
        L(polyiso, LayerFunction.Insulation, EdgeResolution.Butt, 0, 1.0),
        L(ply12, LayerFunction.Sheathing, EdgeResolution.Butt),
        Core(stud2x6, 5.5, mwBatt),
        L(smartVb, LayerFunction.Membrane, EdgeResolution.Wrap, 0.5),
        L(gyp12, LayerFunction.Finish, EdgeResolution.Wrap, 1.0)));

      cat.Assemblies.Add(Assembly("W5", "8\" CMU with interior furred insulation",
        "Load-bearing CMU, continuous XPS, hat channel and gypsum.", 2.0, 52,
        Core(cmu8, 7.625),
        L(airBarrier, LayerFunction.Membrane, EdgeResolution.Wrap, 0.5),
        L(xps, LayerFunction.Insulation, EdgeResolution.Butt, 0, 2.0),
        L(hatChan, LayerFunction.Furring, EdgeResolution.Butt),
        L(gyp58, LayerFunction.Finish, EdgeResolution.Wrap, 1.0)));

      cat.Assemblies.Add(Assembly("W6", "8\" concrete foundation wall, insulated interior",
        "Below-grade wall. Damp-proofing shown as the exterior membrane layer.",
        3.0, 55,
        L(airBarrier, LayerFunction.Membrane, EdgeResolution.Butt),
        Core(conc8, 8.0),
        L(xps, LayerFunction.Insulation, EdgeResolution.Butt, 0, 2.0),
        L(furr15, LayerFunction.Furring, EdgeResolution.Butt),
        L(gyp58, LayerFunction.Finish, EdgeResolution.Wrap, 1.0)));

      cat.OpeningUnits.AddRange(DefaultUnits());

      foreach (var product in cat.Products) ApplySectionDefaults(product);

      cat.SyncLayerNames();
      return cat;
    }

    /// <summary>
    /// Common North American window and door types, called out the way a schedule
    /// does. None name a block: point them at your own block definitions in the
    /// catalog editor and the geometry drops into every opening of that type.
    /// </summary>
    public static List<OpeningUnit> DefaultUnits()
    {
      return new List<OpeningUnit>
      {
        Unit(OpeningKind.Window, "W", "2030 fixed",        24, 36, "Fixed",       0.28, 0.26, 0.52,  380),
        Unit(OpeningKind.Window, "W", "3040 double-hung",  36, 48, "Double-hung", 0.30, 0.30, 0.55,  520),
        Unit(OpeningKind.Window, "W", "3050 double-hung",  36, 60, "Double-hung", 0.30, 0.30, 0.55,  610),
        Unit(OpeningKind.Window, "W", "2646 casement",     30, 54, "Casement",    0.27, 0.28, 0.53,  680),
        Unit(OpeningKind.Window, "W", "5040 slider",       60, 48, "Slider",      0.32, 0.31, 0.55,  790),
        Unit(OpeningKind.Window, "W", "2020 awning",       24, 24, "Awning",      0.29, 0.27, 0.50,  340),

        Unit(OpeningKind.Door,   "D", "3068 entry",        36, 80, "Swing left",  0.21, 0.22, 0.10, 1450),
        Unit(OpeningKind.Door,   "D", "2868 interior",     32, 80, "Swing right", 0.00, 0.00, 0.00,  240),
        Unit(OpeningKind.Door,   "D", "2668 interior",     30, 80, "Swing left",  0.00, 0.00, 0.00,  225),
        Unit(OpeningKind.Door,   "D", "6068 patio slider", 72, 80, "Sliding",     0.30, 0.28, 0.50, 2100),
      };
    }

    static OpeningUnit Unit(OpeningKind kind, string prefix, string name,
                            double width, double height, string operation,
                            double uFactor, double shgc, double vt, double cost)
    {
      return new OpeningUnit
      {
        Kind = kind,
        MarkPrefix = prefix,
        Name = name,
        WidthIn = width,
        HeightIn = height,
        // Half an inch total is the usual shim allowance on a residential unit;
        // a door gets a little more because the frame is set plumb in the opening.
        RoughClearanceIn = kind == OpeningKind.Door ? 0.75 : 0.5,
        Operation = operation,
        Glazing = kind == OpeningKind.Window || uFactor > 0.25 ? "Double, low-E, argon" : "",
        UFactor = uFactor,
        SHGC = shgc,
        VisibleTransmittance = vt,
        Cost = cost
      };
    }

    // ---- small builders ----------------------------------------------------

    static AssemblyLayer L(MaterialProduct product, LayerFunction function,
                           EdgeResolution jamb = EdgeResolution.Butt,
                           double jambReturn = 0.0, double thicknessOverride = 0.0)
    {
      return new AssemblyLayer
      {
        ProductId = product.Id,
        ProductName = product.Name,
        Function = function,
        ThicknessIn = thicknessOverride > 0 ? thicknessOverride : product.ThicknessIn,
        JambResolution = jamb,
        HeadResolution = jamb,
        SillResolution = jamb,
        JambReturnIn = jambReturn,
        HeadReturnIn = jambReturn,
        SillReturnIn = jambReturn
      };
    }

    static AssemblyLayer Core(MaterialProduct product, double thickness,
                              MaterialProduct cavityInsulation = null)
    {
      var l = L(product, LayerFunction.Structure);
      l.ThicknessIn = thickness;
      l.IsCore = true;
      l.IsLoadBearing = true;
      if (cavityInsulation != null)
      {
        l.CavityProductId = cavityInsulation.Id;
        l.CavityProductName = cavityInsulation.Name;
      }
      return l;
    }

    static LayeredAssembly Assembly(string code, string name, string description,
                                 double fireHours, int stc, params AssemblyLayer[] layers)
    {
      var a = new LayeredAssembly
      {
        Code = code,
        Name = name,
        Description = description,
        FireRatingHours = fireHours,
        StcRating = stc
      };
      a.Layers.AddRange(layers);
      a.NormalizeSides();
      return a;
    }
  }
}
