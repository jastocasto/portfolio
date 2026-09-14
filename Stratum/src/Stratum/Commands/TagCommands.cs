using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;

namespace Stratum.Commands
{
  /// <summary>
  /// Labels a wall with a leader that reads the model, and keeps reading it.
  ///
  /// Pick the material you want to name and the note writes itself — "5-1/2&quot;
  /// Wood stud 2x6 @ 16&quot; o.c." — because the picked solid carries its wall id
  /// and its layer index, so the command knows what you pointed at.
  ///
  /// The leader's text is not text. It is a Rhino text field pointing at the
  /// wall's annotation anchor, so when the wall changes the note changes with it:
  /// re-type the wall from 2x6 to 2x4 and every note that named the stud now names
  /// the 2x4, with nothing re-run and nothing re-placed.
  ///
  /// And because Stratum never rewrites these leaders — it only rewrites the data
  /// on the anchor — anything done to one by hand survives. Drag it, restyle it,
  /// replace its text outright: a rebuild cannot touch it.
  /// </summary>
  [Guid("b41d7e52-9a03-4c17-8f6b-2d5e13a90c84")]
  public class BimTagCommand : Command
  {
    public BimTagCommand() { Instance = this; }
    public static BimTagCommand Instance { get; private set; }
    public override string EnglishName => "BimTag";

    /// <summary>What a note can say. The value is a format over anchor keys;
    /// {L} is replaced by the picked layer's prefix, e.g. "L06:".</summary>
    static readonly (string Name, string Pattern)[] Notes =
    {
      ("Material",  "{L}Thickness| |{L}Name"),
      ("Layer",     "{L}Name"),
      ("WallType",  "Stratum:AssemblyCode"),
      ("Assembly",  "Stratum:AssemblyCode| · |Stratum:AssemblyName| · |Stratum:Thickness"),
      ("RValue",    "@R-|Stratum:RValue|@ (|Stratum:RValueEffective|@ eff.)"),
      ("Thickness", "Stratum:Thickness"),
    };

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);
      if (model == null) return Result.Failure;

      // ---- pick the thing to label -----------------------------------------
      var go = new GetObject();
      go.SetCommandPrompt("Pick the material to label");
      go.GeometryFilter = Rhino.DocObjects.ObjectType.Brep;
      go.SubObjectSelect = false;
      go.SetCustomGeometryFilter((rhObj, geo, ci) =>
        rhObj != null && !string.IsNullOrEmpty(rhObj.Attributes.GetUserString(DocKeys.Wall)));

      if (go.Get() != GetResult.Object) return Result.Cancel;

      var objRef = go.Object(0);
      var picked = objRef.Object();
      Point3d head = objRef.SelectionPoint();

      string wallText = picked.Attributes.GetUserString(DocKeys.Wall);
      string layerText = picked.Attributes.GetUserString(DocKeys.LayerIndex);
      Guid wallId;
      if (!Guid.TryParse(wallText, out wallId))
      {
        RhinoApp.WriteLine("Stratum: that solid does not say which wall it belongs to.");
        return Result.Failure;
      }

      var wall = model.Walls.FirstOrDefault(w => w.Id == wallId);
      if (wall == null)
      {
        RhinoApp.WriteLine("Stratum: that solid belongs to a wall this model no longer has. Run BimRebuild.");
        return Result.Failure;
      }

      if (wall.AnchorId == Guid.Empty || doc.Objects.FindId(wall.AnchorId) == null)
      {
        // Walls baked before anchors existed have none until they are rebuilt.
        List<string> w2;
        WallBaker.Rebuild(doc, model, wall, out w2);
        StratumDoc.Set(doc, model);
      }
      if (wall.AnchorId == Guid.Empty)
      {
        RhinoApp.WriteLine("Stratum: this wall has no annotation anchor and could not be rebuilt.");
        return Result.Failure;
      }

      int layerIndex;
      int.TryParse(layerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out layerIndex);
      string prefix = "L" + (layerIndex + 1).ToString("00", CultureInfo.InvariantCulture) + ":";

      // ---- where does it go, and what does it say --------------------------
      int choice = 0;
      var gp = new GetPoint();
      gp.SetCommandPrompt("Where the note goes");
      gp.SetBasePoint(head, true);
      gp.DrawLineFromPoint(head, true);

      var names = Notes.Select(n => n.Name).ToList();
      int optionIndex = gp.AddOptionList("Note", names, choice);

      while (true)
      {
        var res = gp.Get();
        if (res == GetResult.Point) break;
        if (res == GetResult.Option)
        {
          if (gp.OptionIndex() == optionIndex) choice = gp.Option().CurrentListOptionIndex;
          continue;
        }
        return Result.Cancel;
      }

      Point3d tail = gp.Point();
      string formula = Build(Notes[choice].Pattern, prefix, wall.AnchorId);

      // ---- place it --------------------------------------------------------
      uint undo = doc.BeginUndoRecord("BimTag");
      try
      {
        var plane = doc.Views.ActiveView?.ActiveViewport?.ConstructionPlane() ?? Plane.WorldXY;
        plane.Origin = head;

        var leader = Leader.Create("", plane, doc.DimStyles.Current, new[] { head, tail });
        if (leader == null)
        {
          RhinoApp.WriteLine("Stratum: Rhino would not make a leader there.");
          return Result.Failure;
        }
        leader.RichText = formula;   // TextFormula is obsolete; RichText carries the field

        var attributes = new ObjectAttributes();
        int annoLayer = FindLayer(doc, "G-Anno-Labels");
        if (annoLayer >= 0) attributes.LayerIndex = annoLayer;
        attributes.SetUserString("Stratum:TagOf", wall.AnchorId.ToString());

        var id = doc.Objects.AddLeader(leader, attributes);
        if (id == Guid.Empty)
        {
          RhinoApp.WriteLine("Stratum: the leader could not be added.");
          return Result.Failure;
        }

        string shown;
        Rhino.Runtime.TextFields.TryFormat(formula, doc, out shown);
        RhinoApp.WriteLine("Stratum: " + Notes[choice].Name + " note on " +
                           (string.IsNullOrWhiteSpace(wall.Name) ? "wall" : wall.Name) +
                           " reads \"" + shown + "\" and follows the model.");
      }
      finally
      {
        doc.EndUndoRecord(undo);
      }

      doc.Views.Redraw();
      return Result.Success;
    }

    /// <summary>Turns a pattern into a text formula. Segments are split on '|';
    /// one starting with '@' is literal text, anything else is an anchor key.</summary>
    static string Build(string pattern, string layerPrefix, Guid anchor)
    {
      var outp = new System.Text.StringBuilder();
      foreach (var raw in pattern.Split('|'))
      {
        string part = raw.Replace("{L}", layerPrefix);
        if (part.StartsWith("@", StringComparison.Ordinal)) { outp.Append(part.Substring(1)); continue; }
        if (part == " ") { outp.Append(' '); continue; }
        if (!part.Contains(":")) { outp.Append(part); continue; }
        // The id MUST be quoted. Unquoted parses and then resolves to #### forever.
        outp.Append("%<UserText(\"").Append(anchor.ToString()).Append("\",\"").Append(part).Append("\")>%");
      }
      return outp.ToString();
    }

    static int FindLayer(RhinoDoc doc, string name)
    {
      foreach (var l in doc.Layers)
      {
        if (l == null || l.IsDeleted || l.ParentLayerId != Guid.Empty) continue;
        if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l.Index;
      }
      return -1;
    }
  }
}
