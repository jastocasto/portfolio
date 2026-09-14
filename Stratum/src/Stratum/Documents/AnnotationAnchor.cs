using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Documents
{
  /// <summary>
  /// A permanent address for everything there is to say about a wall.
  ///
  /// THE PROBLEM THIS SOLVES
  /// -----------------------
  /// Rhino annotation can read live data out of an object with a text field:
  ///
  ///     %&lt;UserText("2ad1f0c1-…","L06:Thickness")&gt;%
  ///
  /// which is exactly the behaviour wanted - write the dimension once, and it
  /// follows the model. But the field addresses its source BY OBJECT ID, and a
  /// rebuild erases and re-adds every layer solid: measured, 0 of 25 ids survived
  /// a rebuild that changed nothing at all. Point a sheet full of annotation at
  /// the solids and all of it goes to #### the first time a wall is edited.
  ///
  /// So the annotation does not point at the solids. It points here.
  ///
  /// The anchor is one point object per wall, created once and never deleted
  /// while the wall lives. A rebuild rewrites its user text and moves it; its id
  /// does not change. Every field on every sheet keeps resolving, and - the part
  /// that matters - nothing ever rewrites the annotation objects themselves, so
  /// there is no way for a rebuild to destroy work done by hand. Text moved,
  /// text overridden, text suppressed: all of it survives, because regeneration
  /// never touches it. Only the data underneath changes.
  ///
  /// THE TRAP, and it is silent
  /// --------------------------
  /// `obj.Attributes` returns a copy with NO user strings in it. Set one key on
  /// that copy, commit it with ModifyAttributes, and every user string on the
  /// object is wiped. That is why Sync always builds a complete ObjectAttributes
  /// carrying every key rather than editing what is there.
  /// </summary>
  internal static class AnnotationAnchor
  {
    /// <summary>Marks the object as an anchor. Also the key a cleanup pass looks for.</summary>
    internal const string Marker = "Stratum:Anchor";

    /// <summary>Layer the anchors live on, if the document has it. A reference layer
    /// that does not plot is exactly right, and every tds template carries one.
    /// Never created — no layer of ours goes into someone's layer standard.</summary>
    const string PreferredLayer = "G-Ref-NoPlot";

    // ------------------------------------------------------------------ sync

    /// <summary>Creates or updates the wall's anchor. Returns its id, which is
    /// stable for the life of the wall.</summary>
    internal static Guid Sync(RhinoDoc doc, BimModel model, WallDefinition wall,
                              LayeredAssembly assembly)
    {
      if (doc == null || model == null || wall == null) return Guid.Empty;

      Point3d at = Position(wall);
      var attributes = BuildAttributes(doc, model, wall, assembly);

      var existing = wall.AnchorId != Guid.Empty ? doc.Objects.FindId(wall.AnchorId) : null;
      if (existing != null)
      {
        // ORDER MATTERS, and getting it wrong fails silently.
        //
        // Replace supersedes the object: the id is kept, but the RhinoObject that
        // was there is retired. Writing the attributes afterwards - even by id -
        // lands on the retired one and returns true, so the anchor keeps whatever
        // it was first given and every note on the sheet quietly goes stale. That
        // is exactly what happened: the first rebuild wrote it, and no rebuild
        // after that ever did.
        //
        // So: attributes first, onto the object that is actually live.
        doc.Objects.ModifyAttributes(existing, attributes, true);

        // Then move it, and only if it has really moved - a rebuild in place
        // should not be superseding the object at all.
        var point = existing.Geometry as Point;
        if (point == null || point.Location.DistanceTo(at) > 1e-9)
        {
          var live = doc.Objects.FindId(wall.AnchorId);
          if (live != null) doc.Objects.Replace(new ObjRef(live), new Point(at));
        }
        return wall.AnchorId;
      }

      wall.AnchorId = doc.Objects.AddPoint(at, attributes);
      return wall.AnchorId;
    }

    /// <summary>Removes the anchor. Called only when the wall itself is deleted —
    /// never on a rebuild, which is the whole point of it.</summary>
    internal static void Erase(RhinoDoc doc, WallDefinition wall)
    {
      if (doc == null || wall == null || wall.AnchorId == Guid.Empty) return;
      var obj = doc.Objects.FindId(wall.AnchorId);
      if (obj != null) doc.Objects.Delete(obj, true);
      wall.AnchorId = Guid.Empty;
    }

    /// <summary>The text-field formula for one key on this wall, ready to paste into
    /// a leader or a dimension override. The id must be quoted: unquoted parses and
    /// then silently resolves to ####.</summary>
    internal static string Field(WallDefinition wall, string key)
      => wall == null || wall.AnchorId == Guid.Empty
         ? string.Empty
         : "%<UserText(\"" + wall.AnchorId.ToString() + "\",\"" + key + "\")>%";

    // ------------------------------------------------------------- the record

    static ObjectAttributes BuildAttributes(RhinoDoc doc, BimModel model,
                                            WallDefinition wall, LayeredAssembly assembly)
    {
      var a = new ObjectAttributes();
      a.Name = "Stratum anchor · " + (string.IsNullOrWhiteSpace(wall.Name) ? "wall" : wall.Name);

      int layerIndex = FindLayer(doc, PreferredLayer);
      if (layerIndex >= 0) a.LayerIndex = layerIndex;

      a.SetUserString(Marker, "1");
      a.SetUserString(DocKeys.Wall, wall.Id.ToString());
      a.SetUserString(DocKeys.WallName, wall.Name ?? "");

      if (assembly != null)
      {
        a.SetUserString(DocKeys.Assembly, assembly.Id.ToString());
        a.SetUserString(DocKeys.AssemblyCode, assembly.Code ?? "");
        a.SetUserString("Stratum:AssemblyName", assembly.Name ?? "");
        a.SetUserString("Stratum:ThicknessIn", Num(assembly.TotalThicknessIn));
        a.SetUserString("Stratum:Thickness", Imperial(assembly.TotalThicknessIn));
      }

      a.SetUserString("Stratum:LengthIn", Num(wall.Length));
      a.SetUserString("Stratum:Length", Imperial(wall.Length));
      a.SetUserString("Stratum:HeightIn", Num(wall.Height));
      a.SetUserString("Stratum:Height", Imperial(wall.Height));
      a.SetUserString("Stratum:BaseElevationIn", Num(wall.BaseElevation));
      a.SetUserString("Stratum:TopElevationIn", Num(wall.TopElevation));

      // ---- one block of keys per material layer, 1-based to match the drawings
      var layers = assembly == null
        ? new List<AssemblyLayer>()
        : assembly.ActiveLayers.ToList();

      a.SetUserString("Stratum:LayerCount", layers.Count.ToString(CultureInfo.InvariantCulture));

      for (int i = 0; i < layers.Count; i++)
      {
        var layer = layers[i];
        var product = model.Catalog?.FindProduct(layer.ProductId);
        string p = "L" + (i + 1).ToString("00", CultureInfo.InvariantCulture) + ":";

        a.SetUserString(p + "Name", product?.Name ?? layer.ProductName ?? "");
        a.SetUserString(p + "Function", layer.Function.ToString());
        a.SetUserString(p + "ThicknessIn", Num(layer.ThicknessIn));
        a.SetUserString(p + "Thickness", Imperial(layer.ThicknessIn));
        a.SetUserString(p + "IsCore", layer.IsCore ? "yes" : "no");

        if (product != null)
        {
          double r = product.RValueAt(layer.ThicknessIn);
          var cavity = model.Catalog?.FindProduct(layer.CavityProductId);
          if (cavity != null)
          {
            r += cavity.RValueAt(layer.ThicknessIn);
            a.SetUserString(p + "CavityName", cavity.Name ?? "");
          }
          double c = product.CostPerSqFt * (1.0 + product.WasteFactor);
          a.SetUserString(p + "RValue", r.ToString("0.##", CultureInfo.InvariantCulture));
          a.SetUserString(p + "CostPerSF", c.ToString("0.00", CultureInfo.InvariantCulture));
          a.SetUserString(p + "Manufacturer", product.Manufacturer ?? "");
          a.SetUserString(p + "Sku", product.Sku ?? "");
        }
      }
      // Totals come from the assembly itself. The panel already shows R nominal,
      // R effective and U off these same methods, and a second summation here
      // would eventually disagree with what is on screen - which is how a model
      // ends up with two answers to one question.
      if (assembly != null)
      {
        var cat = model.Catalog;
        double rNominal = assembly.RValue(cat);
        double rEff = assembly.EffectiveRValue(cat);
        a.SetUserString("Stratum:RValue", rNominal.ToString("0.0", CultureInfo.InvariantCulture));
        a.SetUserString("Stratum:RValueEffective", rEff.ToString("0.0", CultureInfo.InvariantCulture));
        a.SetUserString("Stratum:UValue", (rEff > 0 ? 1.0 / rEff : 0.0).ToString("0.000", CultureInfo.InvariantCulture));
        a.SetUserString("Stratum:CostPerSF", assembly.CostPerSqFt(cat).ToString("0.00", CultureInfo.InvariantCulture));
        a.SetUserString("Stratum:WeightPsf", assembly.WeightPsf(cat).ToString("0.0", CultureInfo.InvariantCulture));
      }

      // ---- one block per opening
      var openings = wall.Openings ?? new List<Opening>();
      a.SetUserString("Stratum:OpeningCount", openings.Count.ToString(CultureInfo.InvariantCulture));
      for (int i = 0; i < openings.Count; i++)
      {
        var o = openings[i];
        string p = "O" + (i + 1).ToString("00", CultureInfo.InvariantCulture) + ":";
        a.SetUserString(p + "Name", o.Name ?? "");
        a.SetUserString(p + "Kind", o.Kind.ToString());
        a.SetUserString(p + "WidthIn", Num(o.WidthIn));
        a.SetUserString(p + "Width", Imperial(o.WidthIn));
        a.SetUserString(p + "HeightIn", Num(o.HeightIn));
        a.SetUserString(p + "Height", Imperial(o.HeightIn));
        a.SetUserString(p + "SillIn", Num(o.SillHeightIn));
        a.SetUserString(p + "Sill", Imperial(o.SillHeightIn));
        a.SetUserString(p + "HeadIn", Num(o.SillHeightIn + o.HeightIn));
        a.SetUserString(p + "Head", Imperial(o.SillHeightIn + o.HeightIn));
        a.SetUserString(p + "StationIn", Num(o.StationAlongWall));
      }

      return a;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>Midpoint of the baseline, at the wall's base. Near what it describes,
    /// and it travels with the wall.</summary>
    static Point3d Position(WallDefinition wall)
    {
      var c = wall.Baseline;
      if (c == null) return new Point3d(0, 0, wall.BaseElevation);
      var p = c.PointAt(c.Domain.Mid);
      return new Point3d(p.X, p.Y, wall.BaseElevation);
    }

    static int FindLayer(RhinoDoc doc, string name)
    {
      foreach (var l in doc.Layers)
      {
        if (l == null || l.IsDeleted) continue;
        if (l.ParentLayerId != Guid.Empty) continue;
        if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
      }
      return -1;
    }

    static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Inches as a drawing would write them: 5-1/2", 3'-6", 7/16".
    /// Rounded to 1/16, which is the finest anything here is built to.</summary>
    internal static string Imperial(double inches)
    {
      bool neg = inches < 0;
      double v = Math.Abs(inches);

      int sixteenths = (int)Math.Round(v * 16.0, MidpointRounding.AwayFromZero);
      int whole = sixteenths / 16;
      int rem = sixteenths % 16;

      int feet = whole / 12;
      int inch = whole % 12;

      string frac = "";
      if (rem > 0)
      {
        int den = 16;
        while (rem % 2 == 0) { rem /= 2; den /= 2; }
        frac = rem + "/" + den;
      }

      string body;
      if (feet > 0)
      {
        body = feet + "'-" + inch;
        if (frac.Length > 0) body += "-" + frac;
        body += "\"";
      }
      else if (inch > 0)
      {
        body = inch.ToString(CultureInfo.InvariantCulture);
        if (frac.Length > 0) body += "-" + frac;
        body += "\"";
      }
      else
      {
        body = (frac.Length > 0 ? frac : "0") + "\"";
      }

      return neg ? "-" + body : body;
    }
  }
}
