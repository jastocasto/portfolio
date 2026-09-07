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
