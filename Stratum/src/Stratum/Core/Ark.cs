using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Collections;
using Rhino.Geometry;

namespace Stratum.Core
{
  /// <summary>
  /// Thin, defensive helpers over <see cref="ArchivableDictionary"/>.
  ///
  /// Everything Stratum persists - into the .3dm through the plug-in's
  /// WriteDocument override, and into standalone library files - goes through
  /// this class. Only the handful of ArchivableDictionary setters that are
  /// guaranteed stable across Rhino versions are used; arrays are flattened to
  /// invariant-culture strings so that a file written by one Rhino build always
  /// reads back in another.
  /// </summary>
  internal static class Ark
  {
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- write -------------------------------------------------------------

    public static void Put(ArchivableDictionary d, string key, string value)
      => d.Set(key, value ?? string.Empty);

    public static void Put(ArchivableDictionary d, string key, double value)
      => d.Set(key, value);

    public static void Put(ArchivableDictionary d, string key, int value)
      => d.Set(key, value);

    public static void Put(ArchivableDictionary d, string key, bool value)
      => d.Set(key, value);

    public static void Put(ArchivableDictionary d, string key, Guid value)
      => d.Set(key, value.ToString());

    public static void Put(ArchivableDictionary d, string key, ArchivableDictionary value)
      => d.Set(key, value ?? new ArchivableDictionary());

    public static void PutEnum<T>(ArchivableDictionary d, string key, T value) where T : struct
      => d.Set(key, Convert.ToInt32(value));

    public static void PutDoubles(ArchivableDictionary d, string key, IEnumerable<double> values)
      => d.Set(key, string.Join(" ", (values ?? Enumerable.Empty<double>()).Select(v => v.ToString("R", Inv))));

    public static void PutPoints(ArchivableDictionary d, string key, IEnumerable<Point3d> points)
    {
      var flat = new List<double>();
      foreach (var p in points ?? Enumerable.Empty<Point3d>())
      {
        flat.Add(p.X); flat.Add(p.Y); flat.Add(p.Z);
      }
      PutDoubles(d, key, flat);
    }

    /// <summary>Writes an ordered list of child dictionaries as key_count / key_0 / key_1 ...</summary>
    public static void PutList(ArchivableDictionary d, string key, IList<ArchivableDictionary> items)
    {
      d.Set(key + "_count", items?.Count ?? 0);
      if (items == null) return;
      for (int i = 0; i < items.Count; i++)
        d.Set(key + "_" + i.ToString(Inv), items[i]);
    }

    // ---- read --------------------------------------------------------------

    static object Raw(ArchivableDictionary d, string key)
      => (d != null && d.ContainsKey(key)) ? d[key] : null;

    public static string Str(ArchivableDictionary d, string key, string fallback = "")
      => Raw(d, key) as string ?? fallback;

    public static double Num(ArchivableDictionary d, string key, double fallback = 0.0)
    {
      var o = Raw(d, key);
      if (o is double dv) return dv;
      if (o is float fv) return fv;
      if (o is int iv) return iv;
      if (o is string s && double.TryParse(s, NumberStyles.Float, Inv, out var pv)) return pv;
      return fallback;
    }

    public static int Int(ArchivableDictionary d, string key, int fallback = 0)
    {
      var o = Raw(d, key);
      if (o is int iv) return iv;
      if (o is double dv) return (int)Math.Round(dv);
      if (o is string s && int.TryParse(s, NumberStyles.Integer, Inv, out var pv)) return pv;
      return fallback;
    }

    public static bool Bool(ArchivableDictionary d, string key, bool fallback = false)
    {
      var o = Raw(d, key);
      if (o is bool bv) return bv;
      if (o is int iv) return iv != 0;
      if (o is string s && bool.TryParse(s, out var pv)) return pv;
      return fallback;
    }

    public static Guid Id(ArchivableDictionary d, string key)
    {
      var o = Raw(d, key);
      if (o is Guid g) return g;
      if (o is string s && Guid.TryParse(s, out var pg)) return pg;
      return Guid.Empty;
    }

    public static T Enum<T>(ArchivableDictionary d, string key, T fallback) where T : struct
    {
      var o = Raw(d, key);
      if (o == null) return fallback;
      try { return (T)System.Enum.ToObject(typeof(T), Int(d, key, Convert.ToInt32(fallback))); }
      catch { return fallback; }
    }

    public static ArchivableDictionary Dict(ArchivableDictionary d, string key)
      => Raw(d, key) as ArchivableDictionary;

    public static double[] Doubles(ArchivableDictionary d, string key)
    {
      var s = Str(d, key);
      if (string.IsNullOrWhiteSpace(s)) return new double[0];
      var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      var result = new double[parts.Length];
      for (int i = 0; i < parts.Length; i++)
        double.TryParse(parts[i], NumberStyles.Float, Inv, out result[i]);
      return result;
    }

    public static Point3d[] Points(ArchivableDictionary d, string key)
    {
      var flat = Doubles(d, key);
      var pts = new Point3d[flat.Length / 3];
      for (int i = 0; i < pts.Length; i++)
        pts[i] = new Point3d(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
      return pts;
    }

    public static List<ArchivableDictionary> List(ArchivableDictionary d, string key)
    {
      var list = new List<ArchivableDictionary>();
      int count = Int(d, key + "_count", 0);
      for (int i = 0; i < count; i++)
      {
        var child = Dict(d, key + "_" + i.ToString(Inv));
        if (child != null) list.Add(child);
      }
      return list;
    }

    // ---- curves ------------------------------------------------------------
    //
    // Wall baselines are stored as an explicit NURBS description rather than as
    // opaque geometry blobs. It is version-proof, diff-friendly and handles
    // straight, polyline and curved walls with one code path.

    public static void PutCurve(ArchivableDictionary d, string key, Curve curve)
    {
      var nc = curve?.ToNurbsCurve();
      if (nc == null) { d.Set(key + "_cv", 0); return; }

      d.Set(key + "_cv", nc.Points.Count);
      d.Set(key + "_degree", nc.Degree);
      d.Set(key + "_closed", nc.IsClosed);

      var pts = new List<Point3d>();
      var weights = new List<double>();
      for (int i = 0; i < nc.Points.Count; i++)
      {
        var cp = nc.Points[i];
        pts.Add(cp.Location);
        weights.Add(cp.Weight);
      }
      PutPoints(d, key + "_pts", pts);
      PutDoubles(d, key + "_weights", weights);

      var knots = new double[nc.Knots.Count];
      for (int i = 0; i < nc.Knots.Count; i++) knots[i] = nc.Knots[i];
      PutDoubles(d, key + "_knots", knots);
    }

    public static Curve GetCurve(ArchivableDictionary d, string key)
    {
      int cvCount = Int(d, key + "_cv", 0);
      if (cvCount < 2) return null;

      int degree = Math.Max(1, Int(d, key + "_degree", 1));
      var pts = Points(d, key + "_pts");
      var weights = Doubles(d, key + "_weights");
      var knots = Doubles(d, key + "_knots");
      if (pts.Length != cvCount) return null;

      bool rational = weights.Any(w => Math.Abs(w - 1.0) > 1e-12);
      var nc = new NurbsCurve(3, rational, degree + 1, cvCount);
      for (int i = 0; i < cvCount; i++)
      {
        double w = (i < weights.Length && weights[i] > 0) ? weights[i] : 1.0;
        nc.Points.SetPoint(i, pts[i], w);
      }
      if (knots.Length == nc.Knots.Count)
        for (int i = 0; i < knots.Length; i++) nc.Knots[i] = knots[i];
      else
        nc.Knots.CreateUniformKnots(1.0);

      return nc.IsValid ? nc : null;
    }
  }
}
