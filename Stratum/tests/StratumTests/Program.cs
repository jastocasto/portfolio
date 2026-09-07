using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stratum.Core;
using Stratum.Modeling;

namespace Stratum.Tests
{
  /// <summary>
  /// Assertions against the real compiled Stratum assembly.
  ///
  /// These cover the two things that are pure arithmetic and therefore provable
  /// without Rhino: where each layer sits across the wall, and how a typed length
  /// is read. Both are load-bearing. If the justification maths is wrong every
  /// wall in every model is wrong, and if the parser is wrong it is wrong silently.
  /// </summary>
  public static class Program
  {
    static int _checks;
    static readonly List<string> Failures = new List<string>();

    static void Check(string label, bool condition, string detail = "")
    {
      _checks++;
      Console.WriteLine((condition ? "PASS  " : "FAIL  ") + label +
                        (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
      if (!condition) Failures.Add(label);
    }

    static void Near(string label, double actual, double expected, double tol = 1e-9)
      => Check(label, Math.Abs(actual - expected) <= tol,
               $"got {actual.ToString("0.######", CultureInfo.InvariantCulture)}, " +
               $"expected {expected.ToString("0.######", CultureInfo.InvariantCulture)}");

    public static int Main()
    {
      Console.WriteLine("Stratum tests - running against the compiled assembly");
      Console.WriteLine();

      TestNumberParsingAcrossCultures();
      TestBuilderShorthand();
      TestJustificationMaths();
      TestLayerRanges();
      TestSectionDefaults();
      TestTeeJunctions();

      Console.WriteLine();
      if (Failures.Count > 0)
      {
        Console.WriteLine($"{Failures.Count} of {_checks} CHECKS FAILED:");
        foreach (var f in Failures) Console.WriteLine("   - " + f);
        return 1;
      }
      Console.WriteLine($"ALL {_checks} CHECKS PASSED");
      return 0;
    }

    // ------------------------------------------------------------------------

    /// <summary>
    /// The regression test for the bug that mattered: on a comma-decimal locale,
    /// a bare double.Parse("0.75") reads the period as a thousands separator and
    /// returns 75. Half-inch plywood would have become a 75 inch layer.
    /// </summary>
    static void TestNumberParsingAcrossCultures()
    {
      Console.WriteLine("=== 1. numbers parse identically in every locale ===");

      var cultures = new[] { "en-US", "de-DE", "fr-FR", "sv-SE", "en-GB", "" };

      foreach (var name in cultures)
      {
        var culture = string.IsNullOrEmpty(name) ? CultureInfo.InvariantCulture : new CultureInfo(name);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        string label = string.IsNullOrEmpty(name) ? "invariant" : name;

        try
        {
          double v;

          Check($"{label,-9} \"0.75\" parses",
                Units.TryParseNumber("0.75", out v) && Math.Abs(v - 0.75) < 1e-12,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");

          Check($"{label,-9} \"0.75\" is NOT read as 75",
                Units.TryParseNumber("0.75", out v) && v < 1.0,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");

          Check($"{label,-9} \"0,75\" parses to 0.75 or is rejected",
                !Units.TryParseNumber("0,75", out v) || Math.Abs(v - 0.75) < 1e-12,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");

          Check($"{label,-9} \"5.5\" thickness survives",
                Units.TryParseInches("5.5", out v) && Math.Abs(v - 5.5) < 1e-12,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");

          Check($"{label,-9} \"3/4\" survives",
                Units.TryParseInches("3/4", out v) && Math.Abs(v - 0.75) < 1e-12,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");

          Check($"{label,-9} \"140mm\" survives",
                Units.TryParseInches("140mm", out v) && Math.Abs(v - 140.0 / 25.4) < 1e-9,
                $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}");
        }
        finally
        {
          CultureInfo.CurrentCulture = previous;
        }
      }
    }

    static void TestBuilderShorthand()
    {
      Console.WriteLine();
      Console.WriteLine("=== 2. builder shorthand ===");

      var cases = new (string Text, double Expected)[]
      {
        ("3/4", 0.75), ("5 1/2", 5.5), ("5-1/2", 5.5), ("0.75", 0.75),
        ("1'-6\"", 18.0), ("2'", 24.0), ("8'", 96.0), ("7-5/8", 7.625),
        ("140mm", 140.0 / 25.4), ("15cm", 15.0 / 2.54),
      };

      foreach (var c in cases)
      {
        double v;
        Check($"parse {c.Text,-8}", Units.TryParseInches(c.Text, out v) && Math.Abs(v - c.Expected) < 1e-9,
              $"got {v.ToString("0.####", CultureInfo.InvariantCulture)}, expected {c.Expected.ToString("0.####", CultureInfo.InvariantCulture)}");
      }

      double junk;
      Check("garbage is rejected", !Units.TryParseInches("not a size", out junk));
      Check("empty is rejected", !Units.TryParseInches("", out junk));
    }

    // ------------------------------------------------------------------------

    static AssemblyLayer Layer(string name, double thickness, LayerFunction function, bool core = false)
      => new AssemblyLayer
      {
        ProductName = name,
        ThicknessIn = thickness,
        Function = function,
        IsCore = core,
        Enabled = true
      };

    /// <summary>The shipping W1 assembly, with the sheathing and gypsum parameterised.</summary>
    static LayeredAssembly W1(double sheathing = 0.4375, double gypsum = 0.5)
    {
      var a = new LayeredAssembly { Code = "W1", Name = "test" };
      a.Layers.Add(Layer("fiber cement", 0.3125, LayerFunction.Cladding));
      a.Layers.Add(Layer("furring", 0.75, LayerFunction.Furring));
      a.Layers.Add(Layer("WRB", 0.01, LayerFunction.Membrane));
      a.Layers.Add(Layer("polyiso", 2.0, LayerFunction.Insulation));
      a.Layers.Add(Layer("sheathing", sheathing, LayerFunction.Sheathing));
      a.Layers.Add(Layer("2x6 stud", 5.5, LayerFunction.Structure, core: true));
      a.Layers.Add(Layer("smart VB", 0.012, LayerFunction.Membrane));
      a.Layers.Add(Layer("gypsum", gypsum, LayerFunction.Finish));
      return a;
    }

    static (double Exterior, double Interior) Faces(LayeredAssembly a, AssemblyJustification j, bool flipped = false)
    {
      double ext, inte;
      WallSolver.FaceOffsets(a, j, flipped, 1.0, out ext, out inte);
      return (ext, inte);
    }

    static void TestJustificationMaths()
    {
      Console.WriteLine();
      Console.WriteLine("=== 3. the core stays put; faces move on their own side ===");

      // The core's centre line must sit on the baseline for every thickness combination.
      foreach (var sheathing in new[] { 0.4375, 0.5, 0.75, 1.5 })
        foreach (var gypsum in new[] { 0.5, 0.625, 1.25 })
        {
          var a = W1(sheathing, gypsum);
          var ranges = WallSolver.LayerRanges(a, AssemblyJustification.CoreCenter, false, 1.0);
          var core = ranges.Single(r => a.Layers[r.Index].ProductName == "2x6 stud");
          Near($"core centred (sheathing {sheathing}, gypsum {gypsum})", core.Mid, 0.0, 1e-12);
        }

      var baseFaces = Faces(W1(0.5, 0.5), AssemblyJustification.CoreCenter);

      var thickerSheathing = Faces(W1(0.75, 0.5), AssemblyJustification.CoreCenter);
      Near("thickening exterior sheathing moves the exterior face out 1/4\"",
           thickerSheathing.Exterior - baseFaces.Exterior, 0.25);
      Near("...and leaves the interior face alone",
           thickerSheathing.Interior - baseFaces.Interior, 0.0);

      var thickerGypsum = Faces(W1(0.5, 1.25), AssemblyJustification.CoreCenter);
      Near("thickening interior gypsum moves the interior face in 3/4\"",
           baseFaces.Interior - thickerGypsum.Interior, 0.75);
      Near("...and leaves the exterior face alone",
           thickerGypsum.Exterior - baseFaces.Exterior, 0.0);

      foreach (var sheathing in new[] { 0.4375, 0.75, 1.5 })
        Near($"FirstFace pins the exterior face (sheathing {sheathing})",
             Faces(W1(sheathing), AssemblyJustification.FirstFace).Exterior, 0.0, 1e-12);

      foreach (var gypsum in new[] { 0.5, 1.25 })
        Near($"LastFace pins the interior face (gypsum {gypsum})",
             Faces(W1(0.5, gypsum), AssemblyJustification.LastFace).Interior, 0.0, 1e-12);
    }

    /// <summary>
    /// The rules that decide how each material reads on a section cut. Pure logic,
    /// so it can be pinned down here rather than discovered by cutting a section
    /// and squinting at it.
    /// </summary>
    static void TestSectionDefaults()
    {
      Console.WriteLine();
      Console.WriteLine("=== 5. every material knows how it reads on a section cut ===");

      var catalog = CatalogDefaults.Create();

      Check("every seeded product has section settings",
            catalog.Products.All(p => p.SectionLineWeightScale > 0 && p.SectionHatchScale > 0));

      var expectations = new (string ProductContains, string Pattern)[]
      {
        ("CMU",                    SectionPatternNames.Masonry),
        ("Cast-in-place concrete", SectionPatternNames.Concrete),
        ("Modular brick",          SectionPatternNames.Brick),
        ("Fiberglass batt",        SectionPatternNames.BattInsulation),
        ("XPS rigid",              SectionPatternNames.RigidInsulation),
        ("Polyisocyanurate",       SectionPatternNames.RigidInsulation),
        ("Gypsum board",           SectionPatternNames.Gypsum),
        ("Plywood CDX",            SectionPatternNames.Wood),
        ("Wood stud",              SectionPatternNames.Wood),
        ("Steel stud",             SectionPatternNames.Steel),
        ("Standing seam metal",    SectionPatternNames.Steel),
      };

      foreach (var e in expectations)
      {
        var product = catalog.Products.FirstOrDefault(p => p.Name.Contains(e.ProductContains));
        Check($"{e.ProductContains,-24} hatches as {e.Pattern.Replace("Stratum ", "")}",
              product != null && product.SectionHatchPattern == e.Pattern,
              product == null ? "product not found" : "got '" + product.SectionHatchPattern + "'");
      }

      // Membranes and cavities are too thin to hatch legibly and are drawn as a line.
      foreach (var category in new[] { "Membrane", "Air Gap" })
      {
        var products = catalog.Products.Where(p => p.Category == category).ToList();
        Check($"{category,-9} products are poche only",
              products.Count > 0 && products.All(p => string.IsNullOrEmpty(p.SectionHatchPattern)));
        Check($"{category,-9} products cut with a light line",
              products.All(p => p.SectionLineWeightScale < 1.0));
      }

      Check("structure cuts heavier than finishes",
            catalog.Products.Where(p => p.Category == "Structure").All(p => p.SectionLineWeightScale > 1.0) &&
            catalog.Products.Where(p => p.Category == "Finish").All(p => p.SectionLineWeightScale <= 1.0));

      // The poche must differ from the model colour, or a section reads as a
      // flat shaded picture rather than a drawing.
      var gypsum = catalog.Products.First(p => p.Name.Contains("Gypsum board"));
      Check("poche is lighter than the model colour",
            gypsum.SectionFillColor.GetBrightness() > gypsum.Color.GetBrightness() - 0.001);
      Check("hatch lines are darker than the model colour",
            gypsum.SectionHatchColor.GetBrightness() < gypsum.Color.GetBrightness());

      // Every pattern the catalog names must be one the builder can actually make.
      var known = new HashSet<string>(SectionPatternNames.All);
      Check("no product names a pattern that does not exist",
            catalog.Products.All(p => known.Contains(p.SectionHatchPattern ?? "")));
    }

    /// <summary>A simple partition: gypsum, stud core, gypsum.</summary>
    static LayeredAssembly Partition(double stud = 3.5, double gypsum = 0.625)
    {
      var a = new LayeredAssembly { Code = "W2", Name = "partition" };
      a.Layers.Add(Layer("gypsum", gypsum, LayerFunction.Finish));
      a.Layers.Add(Layer("2x4 stud", stud, LayerFunction.Structure, core: true));
      a.Layers.Add(Layer("gypsum", gypsum, LayerFunction.Finish));
      return a;
    }

    /// <summary>
    /// The tee rule, which was a deliberate decision: a partition meeting an
    /// exterior wall ALWAYS ties to structure. Its core runs through the through
    /// wall's finish layers to land on that wall's core; its own finish layers stop
    /// at the through wall's face; and the through wall is notched over the width of
    /// the arriving core so the two do not occupy the same space.
    /// </summary>
    static void TestTeeJunctions()
    {
      Console.WriteLine();
      Console.WriteLine("=== 6. tees tie to structure ===");

      var through = W1();            // 2x6 exterior wall, core centred
      var stem = Partition();

      // W1 layers, exterior -> interior: cladding .3125, furring .75, WRB .01,
      // polyiso 2, sheathing .4375, CORE 5.5, VB .012, gypsum .5
      // With CoreCenter the core spans -2.75 .. +2.75 and the interior face is at
      // -(2.75 + 0.012 + 0.5) = -3.262.
      double toCore, toFace, notchFrom, notchTo, notchWidth;

      // The stem arrives from the interior side, which is negative in W1's frame.
      bool ok = WallJoiner.TeeDistances(
        through, AssemblyJustification.CoreCenter, false,
        stem, AssemblyJustification.CoreCenter, false,
        -1.0, 1.0,
        out toCore, out toFace, out notchFrom, out notchTo, out notchWidth);

      Check("a tee from the interior resolves", ok);
      Near("stem core reaches the through wall's core face", toCore, 2.75);
      Near("stem finish layers stop at the through wall's face", toFace, 3.262);
      Check("the core runs further than the finish layers", toCore < toFace,
            $"core {toCore.ToString("0.###", CultureInfo.InvariantCulture)}, " +
            $"face {toFace.ToString("0.###", CultureInfo.InvariantCulture)}");
      Near("the notch clears from the core face...", notchFrom, -2.75);
      Near("...out to the interior face", notchTo, -3.262);
      Near("the notch is as wide as the arriving core", notchWidth, 3.5);

      // Same wall, stem arriving from the exterior side.
      ok = WallJoiner.TeeDistances(
        through, AssemblyJustification.CoreCenter, false,
        stem, AssemblyJustification.CoreCenter, false,
        +1.0, 1.0,
        out toCore, out toFace, out notchFrom, out notchTo, out notchWidth);

      Check("a tee from the exterior resolves", ok);
      Near("stem core still reaches the core face", toCore, 2.75);
      // Exterior face = everything outside the core, plus half the core.
      Near("stem finish stops at the exterior face",
           toFace, 0.3125 + 0.75 + 0.01 + 2.0 + 0.4375 + 2.75);
      Near("the notch mirrors to the exterior side", notchFrom, 2.75);
      Near("the notch width is unchanged", notchWidth, 3.5);

      // A thicker partition notches a wider hole; the depth does not change.
      WallJoiner.TeeDistances(through, AssemblyJustification.CoreCenter, false,
        Partition(5.5), AssemblyJustification.CoreCenter, false, -1.0, 1.0,
        out toCore, out toFace, out notchFrom, out notchTo, out notchWidth);
      Near("a 2x6 partition notches a 5-1/2\" hole", notchWidth, 5.5);
      Near("...and still lands on the same core face", toCore, 2.75);

      // Re-justifying the through wall must not change where its core face is in
      // space relative to the stem - only the numbers measured from its baseline.
      double coreExtFace, faceExtFace, dummy1, dummy2, dummy3;
      WallJoiner.TeeDistances(through, AssemblyJustification.FirstFace, false,
        stem, AssemblyJustification.CoreCenter, false, -1.0, 1.0,
        out coreExtFace, out faceExtFace, out dummy1, out dummy2, out dummy3);
      Near("core-to-face distance is justification independent",
           faceExtFace - coreExtFace, toFace - toCore);

      // A partition with no separate finish (core only) stops in one place.
      var bare = new LayeredAssembly { Code = "W0", Name = "bare" };
      bare.Layers.Add(Layer("concrete", 8.0, LayerFunction.Structure, core: true));
      WallJoiner.TeeDistances(through, AssemblyJustification.CoreCenter, false,
        bare, AssemblyJustification.CoreCenter, false, -1.0, 1.0,
        out toCore, out toFace, out notchFrom, out notchTo, out notchWidth);
      Near("a core-only wall notches its full thickness", notchWidth, 8.0);

      // A notch must actually bite the layers it is meant to and leave the core alone.
      var notch = new WallNotch { Station = 0, Width = 3.5, FromOffset = -2.75, ToOffset = -3.262 };
      Check("the notch bites the interior gypsum", notch.Touches(-3.262, -2.762, 1e-9));
      Check("the notch leaves the structural core alone", !notch.Touches(-2.75, 2.75, 1e-9));
      Check("the notch leaves the exterior cladding alone", !notch.Touches(6.0, 6.3125, 1e-9));
    }

    static void TestLayerRanges()
    {
      Console.WriteLine();
      Console.WriteLine("=== 4. layer ranges are contiguous, complete and mirror on flip ===");

      foreach (AssemblyJustification j in Enum.GetValues(typeof(AssemblyJustification)))
        foreach (var flipped in new[] { false, true })
        {
          var a = W1();
          var ranges = WallSolver.LayerRanges(a, j, flipped, 1.0);

          double covered = ranges.Sum(r => r.Thickness);
          Near($"{j,-13} flip={flipped,-5} covers the full thickness", covered, a.TotalThicknessIn);

          var sorted = ranges.OrderBy(r => r.Low).ToList();
          bool contiguous = true;
          for (int i = 0; i < sorted.Count - 1; i++)
            if (Math.Abs(sorted[i].High - sorted[i + 1].Low) > 1e-12) contiguous = false;
          Check($"{j,-13} flip={flipped,-5} no gaps or overlaps", contiguous);
        }

      var straight = WallSolver.LayerRanges(W1(), AssemblyJustification.CoreCenter, false, 1.0);
      var mirrored = WallSolver.LayerRanges(W1(), AssemblyJustification.CoreCenter, true, 1.0);
      bool isMirror = straight.Count == mirrored.Count &&
        Enumerable.Range(0, straight.Count).All(i =>
          Math.Abs(straight[i].Low + mirrored[i].High) < 1e-12 &&
          Math.Abs(straight[i].High + mirrored[i].Low) < 1e-12);
      Check("flipping is an exact mirror about the baseline", isMirror);

      // A disabled layer must drop out without disturbing the core.
      var withoutCladding = W1();
      withoutCladding.Layers[0].Enabled = false;
      var reduced = WallSolver.LayerRanges(withoutCladding, AssemblyJustification.CoreCenter, false, 1.0);
      var coreRange = reduced.Single(r => withoutCladding.Layers[r.Index].ProductName == "2x6 stud");
      Near("core still centred after disabling the cladding", coreRange.Mid, 0.0, 1e-12);
      Near("total thickness drops by exactly the cladding",
           withoutCladding.TotalThicknessIn, W1().TotalThicknessIn - 0.3125);
    }
  }
}
