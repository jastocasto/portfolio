using System;
using System.Globalization;
using Rhino;

namespace Stratum.Core
{
  /// <summary>
  /// The product catalog is authored in inches because that is how North American
  /// building products are specified. Geometry is built in model units. Every
  /// conversion in Stratum goes through here so a metric document and an imperial
  /// document can share the same catalog.
  /// </summary>
  public static class Units
  {
    public static double InchToModel(RhinoDoc doc)
    {
      if (doc == null) return 1.0;
      var us = doc.ModelUnitSystem;
      if (us == UnitSystem.None || us == UnitSystem.Unset) return 1.0;
      return RhinoMath.UnitScale(UnitSystem.Inches, us);
    }

    public static double ModelToInch(RhinoDoc doc)
    {
      double s = InchToModel(doc);
      return Math.Abs(s) > RhinoMath.ZeroTolerance ? 1.0 / s : 1.0;
    }

    public static double FeetToModel(RhinoDoc doc) => InchToModel(doc) * 12.0;

    /// <summary>Square feet of face area for a length and height given in model units.</summary>
    public static double SquareFeet(RhinoDoc doc, double lengthModel, double heightModel)
    {
      double toInch = ModelToInch(doc);
      return (lengthModel * toInch) * (heightModel * toInch) / 144.0;
    }

    /// <summary>Formats a length given in inches the way a builder would read it,
    /// e.g. 5.5 -> 5 1/2". Metric documents get millimetres instead.</summary>
    public static string FormatInches(RhinoDoc doc, double inches)
    {
      if (doc != null && !IsImperial(doc.ModelUnitSystem))
        return (inches * 25.4).ToString("0.#", CultureInfo.InvariantCulture) + " mm";

      bool negative = inches < 0;
      double v = Math.Abs(inches);
      int whole = (int)Math.Floor(v + 1e-9);
      double frac = v - whole;

      // Snap to the nearest 1/32 - finer than that is noise on a construction drawing.
      int num = (int)Math.Round(frac * 32.0);
      int den = 32;
      if (num == 32) { whole += 1; num = 0; }
      while (num > 0 && num % 2 == 0) { num /= 2; den /= 2; }

      string s;
      if (num == 0) s = whole.ToString(CultureInfo.InvariantCulture);
      else if (whole == 0) s = num + "/" + den;
      else s = whole + "-" + num + "/" + den;

      return (negative ? "-" : "") + s + "\"";
    }

    /// <summary>
    /// Formats inches as feet and inches the way a window schedule calls them out:
    /// 36 becomes 3'-0", 30 becomes 2'-6", 36.5 becomes 3'-0 1/2".
    ///
    /// Document-free on purpose, so schedules and catalog entries can be formatted
    /// without a RhinoDoc to hand.
    /// </summary>
    public static string FeetInchesShort(double inches)
    {
      bool negative = inches < 0;
      double v = Math.Abs(inches);

      int feet = (int)Math.Floor(v / 12.0 + 1e-9);
      double rest = v - feet * 12.0;

      int whole = (int)Math.Floor(rest + 1e-9);
      int num = (int)Math.Round((rest - whole) * 16.0);
      int den = 16;
      if (num == 16) { whole += 1; num = 0; }
      if (whole == 12) { feet += 1; whole = 0; }
      while (num > 0 && num % 2 == 0) { num /= 2; den /= 2; }

      string inchPart = whole.ToString(CultureInfo.InvariantCulture);
      if (num > 0) inchPart += " " + num + "/" + den;

      return (negative ? "-" : "") + feet.ToString(CultureInfo.InvariantCulture) + "'-" + inchPart + "\"";
    }

    public static bool IsImperial(UnitSystem us)
    {
      return us == UnitSystem.Inches || us == UnitSystem.Feet ||
             us == UnitSystem.Miles || us == UnitSystem.Yards;
    }

    /// <summary>
    /// Parses one number the way a person actually types it, wherever they are.
    ///
    /// This deliberately does NOT use the ambient culture alone. On a locale whose
    /// decimal separator is a comma, plain double.Parse("0.75") reads the period as
    /// a THOUSANDS separator and returns 75 - so an architect in Berlin typing the
    /// thickness of 3/4" plywood would silently get a 75 inch wall. NumberStyles.Float
    /// excludes thousands separators entirely, which makes that misreading impossible,
    /// and trying invariant before the local convention means both "0.75" and "0,75"
    /// are understood everywhere.
    /// </summary>
    public static bool TryParseNumber(string text, out double value)
    {
      value = 0.0;
      if (string.IsNullOrWhiteSpace(text)) return false;

      var t = text.Trim();
      return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
          || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>Parses builder shorthand back to inches: 5 1/2, 5-1/2, 5.5, 1'-6", 140mm.</summary>
    public static bool TryParseInches(string text, out double inches)
    {
      inches = 0.0;
      if (string.IsNullOrWhiteSpace(text)) return false;

      var s = text.Trim().ToLowerInvariant().Replace("\"", "").Replace("in", "").Trim();

      if (s.EndsWith("mm", StringComparison.Ordinal))
      {
        if (TryParseNumber(s.Substring(0, s.Length - 2), out var mm)) { inches = mm / 25.4; return true; }
        return false;
      }
      if (s.EndsWith("cm", StringComparison.Ordinal))
      {
        if (TryParseNumber(s.Substring(0, s.Length - 2), out var cm)) { inches = cm / 2.54; return true; }
        return false;
      }

      double total = 0.0;

      // Feet part: 1'-6 or 1' 6
      int tick = s.IndexOf('\'');
      if (tick >= 0)
      {
        if (TryParseNumber(s.Substring(0, tick), out var ft)) total += ft * 12.0;
        s = s.Substring(tick + 1).TrimStart('-', ' ');
      }

      if (s.Length == 0) { inches = total; return true; }

      s = s.Replace('-', ' ');
      var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var part in parts)
      {
        if (part.Contains("/"))
        {
          var fp = part.Split('/');
          if (fp.Length == 2 && TryParseNumber(fp[0], out var n) && TryParseNumber(fp[1], out var dd) && Math.Abs(dd) > 1e-9)
            total += n / dd;
          else return false;
        }
        else if (TryParseNumber(part, out var whole)) total += whole;
        else return false;
      }

      inches = total;
      return true;
    }
  }
}
