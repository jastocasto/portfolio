using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;

namespace Stratum.Commands
{
  /// <summary>
  /// Manages building levels: the named elevations walls are built from.
  ///
  /// Changing a level's elevation moves every wall bound to it, which is the whole
  /// reason for having levels rather than typed elevations.
  /// </summary>
  [Guid("6bcfc324-e1cc-4459-a3fc-1a25a030d57a")]
  public class BimLevelsCommand : Command
  {
    public BimLevelsCommand() { Instance = this; }
    public static BimLevelsCommand Instance { get; private set; }

    public override string EnglishName => "BimLevels";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      while (true)
      {
        ListLevels(doc, model);

        var go = new GetOption();
        go.SetCommandPrompt("Levels");
        int optAdd = go.AddOption("Add");
        int optRename = go.AddOption("Rename");
        int optMove = go.AddOption("SetElevation");
        int optDelete = go.AddOption("Delete");
        go.AcceptNothing(true);

        var res = go.Get();
        if (res == Rhino.Input.GetResult.Nothing) return Result.Success;
        if (res != Rhino.Input.GetResult.Option) return Result.Cancel;

        var option = go.Option();
        if (option == null) continue;

        if (option.Index == optAdd) { if (!Add(doc, model)) return Result.Cancel; }
        else if (option.Index == optRename) { if (!Rename(doc, model)) return Result.Cancel; }
        else if (option.Index == optMove) { if (!SetElevation(doc, model)) return Result.Cancel; }
        else if (option.Index == optDelete) { if (!Delete(doc, model)) return Result.Cancel; }
      }
    }

    static void ListLevels(RhinoDoc doc, BimModel model)
    {
      RhinoApp.WriteLine("Stratum levels:");
      foreach (var level in model.SortedLevels)
      {
        int walls = model.Walls.Count(w => w.LevelId == level.Id);
        RhinoApp.WriteLine(string.Format(CultureInfo.CurrentCulture,
          "   {0,-18} {1,10}   {2} wall{3}",
          level.Name,
          Units.FormatInches(doc, level.Elevation * Units.ModelToInch(doc)),
          walls, walls == 1 ? "" : "s"));
      }
    }

    static bool PickLevel(BimModel model, string prompt, out Level picked)
    {
      picked = null;
      var levels = model.SortedLevels.ToList();
      if (levels.Count == 0) return false;

      var names = levels.Select(l => CommandUtil.Sanitize(l.Name)).ToArray();

      var go = new GetOption();
      go.SetCommandPrompt(prompt);
      int list = go.AddOptionList("Level", names, 0);

      if (go.Get() != Rhino.Input.GetResult.Option) return false;
      var option = go.Option();
      if (option == null || option.Index != list) return false;

      picked = levels[Math.Max(0, Math.Min(levels.Count - 1, option.CurrentListOptionIndex))];
      return true;
    }

    static bool Add(RhinoDoc doc, BimModel model)
    {
      var gs = new GetString();
      gs.SetCommandPrompt("Name for the new level");
      gs.SetDefaultString("Level " + (model.Levels.Count + 1).ToString(CultureInfo.InvariantCulture));
      if (gs.Get() != Rhino.Input.GetResult.String) return false;

      double elevation;
      if (!GetElevation(doc, "Elevation of the level", 0.0, out elevation)) return false;

      model.Levels.Add(new Level
      {
        Name = gs.StringResult().Trim(),
        Elevation = elevation,
        SortOrder = model.Levels.Count
      });

      StratumDoc.RaiseModelChanged(doc);
      return true;
    }

    static bool Rename(RhinoDoc doc, BimModel model)
    {
      Level level;
      if (!PickLevel(model, "Level to rename", out level)) return false;

      var gs = new GetString();
      gs.SetCommandPrompt("New name");
      gs.SetDefaultString(level.Name);
      if (gs.Get() != Rhino.Input.GetResult.String) return false;

      level.Name = gs.StringResult().Trim();
      StratumDoc.RaiseModelChanged(doc);
      return true;
    }

    static bool SetElevation(RhinoDoc doc, BimModel model)
    {
      Level level;
      if (!PickLevel(model, "Level to move", out level)) return false;

      double elevation;
      if (!GetElevation(doc, "New elevation", level.Elevation, out elevation)) return false;

      level.Elevation = elevation;

      // Everything bound to this level, and anything building up to it, moves.
      var affected = model.Walls
        .Where(w => w.LevelId == level.Id || w.TopLevelId == level.Id)
        .ToList();

      uint undo = doc.BeginUndoRecord("Move level");
      try
      {
        List<string> warnings;
        WallBaker.RebuildMany(doc, model, affected, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally { doc.EndUndoRecord(undo); }

      RhinoApp.WriteLine("Stratum: '{0}' moved; {1} walls rebuilt.", level.Name, affected.Count);
      StratumDoc.RaiseModelChanged(doc);
      return true;
    }

    static bool Delete(RhinoDoc doc, BimModel model)
    {
      if (model.Levels.Count <= 1)
      {
        RhinoApp.WriteLine("Stratum: a document needs at least one level.");
        return true;
      }

      Level level;
      if (!PickLevel(model, "Level to delete", out level)) return false;

      int inUse = model.Walls.Count(w => w.LevelId == level.Id || w.TopLevelId == level.Id);
      if (inUse > 0)
      {
        RhinoApp.WriteLine("Stratum: {0} walls still use '{1}'. Move them to another level first.",
                           inUse, level.Name);
        return true;
      }

      model.Levels.Remove(level);
      StratumDoc.RaiseModelChanged(doc);
      return true;
    }

    /// <summary>Elevations are typed the way they are called out - 9', 9'-1 1/8", 2750mm.</summary>
    static bool GetElevation(RhinoDoc doc, string prompt, double currentModel, out double elevationModel)
    {
      elevationModel = currentModel;

      var gs = new GetString();
      gs.SetCommandPrompt(prompt);
      gs.SetDefaultString(Units.FormatInches(doc, currentModel * Units.ModelToInch(doc)));
      if (gs.Get() != Rhino.Input.GetResult.String) return false;

      double inches;
      if (!Units.TryParseInches(gs.StringResult(), out inches))
      {
        RhinoApp.WriteLine("Stratum: couldn't read that as a height. Try 9', 9'-1 1/8\" or 2750mm.");
        return false;
      }

      elevationModel = inches * Units.InchToModel(doc);
      return true;
    }
  }

  /// <summary>
  /// Sets how the top of a wall is decided: a plain height, up to a level, or cut
  /// against a surface so the wall rakes up to meet a roof at its own angle.
  /// </summary>
  [Guid("91b32d48-e0f3-4c68-b903-90af6e0a9b96")]
  public class BimWallTopCommand : Command
  {
    public BimWallTopCommand() { Instance = this; }
    public static BimWallTopCommand Instance { get; private set; }

    public override string EnglishName => "BimWallTop";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var go = new GetObject();
      go.SetCommandPrompt("Select walls to set the top condition for");
      go.GeometryFilter = ObjectType.Brep;
      go.SubObjectSelect = false;
      go.EnablePreSelect(true, true);
      go.SetCustomGeometryFilter((rhObject, geometry, componentIndex) =>
        rhObject != null && !string.IsNullOrEmpty(rhObject.Attributes.GetUserString(DocKeys.Wall)));
      go.GetMultiple(1, 0);
      if (go.CommandResult() != Result.Success) return go.CommandResult();

      var walls = new List<WallDefinition>();
      foreach (var objRef in go.Objects())
      {
        var wall = StratumDoc.WallOf(doc, objRef.Object());
        if (wall != null && !walls.Any(w => w.Id == wall.Id)) walls.Add(wall);
      }
      if (walls.Count == 0) return Result.Nothing;

      var gm = new GetOption();
      gm.SetCommandPrompt("Top condition");
      int optHeight = gm.AddOption("Height");
      int optLevel = gm.AddOption("ToLevel");
      int optSurface = gm.AddOption("ToSurface");

      if (gm.Get() != Rhino.Input.GetResult.Option) return Result.Cancel;
      var chosen = gm.Option();
      if (chosen == null) return Result.Cancel;

      uint undo = doc.BeginUndoRecord("BimWallTop");
      try
      {
        if (chosen.Index == optHeight)
        {
          var height = new OptionDouble(walls[0].Height, true, 0.0);
          var gh = new GetOption();
          gh.SetCommandPrompt("Wall height");
          gh.AddOptionDouble("Height", ref height);
          gh.AcceptNothing(true);
          gh.Get();

          foreach (var wall in walls)
          {
            wall.TopMode = WallTopMode.Height;
            wall.Height = height.CurrentValue;
          }
        }
        else if (chosen.Index == optLevel)
        {
          var levels = model.SortedLevels.ToList();
          if (levels.Count < 2)
          {
            RhinoApp.WriteLine("Stratum: add a second level first with BimLevels.");
            return Result.Nothing;
          }

          var names = levels.Select(l => CommandUtil.Sanitize(l.Name)).ToArray();
          var gl = new GetOption();
          gl.SetCommandPrompt("Build up to which level?");
          int list = gl.AddOptionList("Level", names, levels.Count - 1);
          if (gl.Get() != Rhino.Input.GetResult.Option) return Result.Cancel;

          var picked = levels[Math.Max(0, gl.Option().CurrentListOptionIndex)];
          foreach (var wall in walls)
          {
            wall.TopMode = WallTopMode.ToLevel;
            wall.TopLevelId = picked.Id;
          }
        }
        else if (chosen.Index == optSurface)
        {
          var gs = new GetObject();
          gs.SetCommandPrompt("Select the surface to cap the walls against (a roof plane)");
          gs.GeometryFilter = ObjectType.Surface | ObjectType.Brep | ObjectType.Extrusion;
          gs.SubObjectSelect = false;
          gs.SetCustomGeometryFilter((rhObject, geometry, componentIndex) =>
            rhObject != null && string.IsNullOrEmpty(rhObject.Attributes.GetUserString(DocKeys.Wall)));
          if (gs.Get() != Rhino.Input.GetResult.Object) return Result.Cancel;

          var target = gs.Object(0).ObjectId;
          foreach (var wall in walls)
          {
            wall.TopMode = WallTopMode.ToSurface;
            wall.TopSurfaceObjectId = target;
          }

          RhinoApp.WriteLine("Stratum: walls now cap against that surface. " +
                             "Edit the surface and run BimRebuild to re-cut them.");
        }

        List<string> warnings;
        WallBaker.RebuildMany(doc, model, walls, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally { doc.EndUndoRecord(undo); }

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      return Result.Success;
    }
  }
}
