using System;
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
        return (inches * 25.4).ToString("0.#") + " mm";

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
      if (num == 0) s = whole.ToString();
      else if (whole == 0) s = num + "/" + den;
      else s = whole + "-" + num + "/" + den;

      return (negative ? "-" : "") + s + "\"";
    }

    public static bool IsImperial(UnitSystem us)
    {
      return us == UnitSystem.Inches || us == UnitSystem.Feet ||
             us == UnitSystem.Miles || us == UnitSystem.Yards;
    }

    /// <summary>Parses builder shorthand back to inches: 5 1/2, 5-1/2, 5.5, 1'-6", 140mm.</summary>
    public static bool TryParseInches(string text, out double inches)
    {
      inches = 0.0;
      if (string.IsNullOrWhiteSpace(text)) return false;

      var s = text.Trim().ToLowerInvariant().Replace("\"", "").Replace("in", "").Trim();

      if (s.EndsWith("mm")) 
      {
        if (double.TryParse(s.Substring(0, s.Length - 2).Trim(), out var mm)) { inches = mm / 25.4; return true; }
        return false;
      }
      if (s.EndsWith("cm"))
      {
        if (double.TryParse(s.Substring(0, s.Length - 2).Trim(), out var cm)) { inches = cm / 2.54; return true; }
        return false;
      }

      double total = 0.0;

      // Feet part: 1'-6 or 1' 6
      int tick = s.IndexOf('\'');
      if (tick >= 0)
      {
        if (double.TryParse(s.Substring(0, tick).Trim(), out var ft)) total += ft * 12.0;
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
          if (fp.Length == 2 && double.TryParse(fp[0], out var n) && double.TryParse(fp[1], out var dd) && Math.Abs(dd) > 1e-9)
            total += n / dd;
          else return false;
        }
        else if (double.TryParse(part, out var whole)) total += whole;
        else return false;
      }

      inches = total;
      return true;
    }
  }
}
