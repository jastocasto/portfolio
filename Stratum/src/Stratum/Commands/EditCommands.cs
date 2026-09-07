using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Modeling;
using Stratum.Ui;

namespace Stratum.Commands
{
  /// <summary>Retypes and re-parameterises existing walls from the command line.
  /// Everything here is also available in the BIM Wall panel; the command line
  /// version exists because that is how Rhino users script and batch things.</summary>
  [Guid("f6c52c77-2854-4b11-b76b-982ee1f2395c")]
  public class BimWallEditCommand : Command
  {
    public BimWallEditCommand() { Instance = this; }
    public static BimWallEditCommand Instance { get; private set; }
    public override string EnglishName => "BimWallEdit";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var go = new GetObject();
      go.SetCommandPrompt("Select walls to edit");
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

      var first = walls[0];
      var codes = CommandUtil.AssemblyCodes(model);
      int assemblyIndex = Math.Max(0, model.Catalog.Assemblies.FindIndex(a => a.Id == first.AssemblyId));
      int justIndex = (int)first.Justification;

      var heightOption = new OptionDouble(first.Height, true, 0.0);
      var baseOption = new OptionDouble(first.BaseElevation);

      var gs = new GetOption();
      gs.SetCommandPrompt(string.Format("{0} wall{1} selected. Change what?",
                                        walls.Count, walls.Count == 1 ? "" : "s"));
      int optAssembly = gs.AddOptionList("Assembly", codes, assemblyIndex);
      int optJust = gs.AddOptionList("Justification",
        CommandUtil.JustificationNames(model.Catalog.Assemblies[assemblyIndex]), justIndex);
      gs.AddOptionDouble("Height", ref heightOption);
      gs.AddOptionDouble("BaseElevation", ref baseOption);
      int optFlip = gs.AddOption("Flip");
      int optPanel = gs.AddOption("OpenPanel");
      gs.AcceptNothing(true);

      bool flip = false;
      while (true)
      {
        var result = gs.Get();
        if (result == Rhino.Input.GetResult.Nothing) break;
        if (result != Rhino.Input.GetResult.Option) return Result.Cancel;

        var option = gs.Option();
        if (option == null) continue;

        if (option.Index == optAssembly) assemblyIndex = option.CurrentListOptionIndex;
        else if (option.Index == optJust) justIndex = option.CurrentListOptionIndex;
        else if (option.Index == optFlip) flip = !flip;
        else if (option.Index == optPanel)
        {
          WallPanel.Show();
          break;
        }
      }

      uint undoRecord = doc.BeginUndoRecord("BimWallEdit");
      try
      {
        foreach (var wall in walls)
        {
          wall.AssemblyId = model.Catalog.Assemblies[assemblyIndex].Id;
          wall.Justification = (AssemblyJustification)justIndex;
          wall.Height = heightOption.CurrentValue;
          wall.BaseElevation = baseOption.CurrentValue;
          if (flip) wall.Flipped = !wall.Flipped;
        }

        List<string> warnings;
        WallBaker.RebuildMany(doc, model, walls, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      return Result.Success;
    }
  }

  /// <summary>Opens the BIM Wall properties panel.</summary>
  [Guid("6151e753-1f30-4be7-8019-3eb36b8abeec")]
  public class BimWallPropertiesCommand : Command
  {
    public BimWallPropertiesCommand() { Instance = this; }
    public static BimWallPropertiesCommand Instance { get; private set; }
    public override string EnglishName => "BimWallProperties";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      CommandUtil.Prepare(doc);
      WallPanel.Show();
      return Result.Success;
    }
  }

  /// <summary>Opens the wall type and product catalog editor.</summary>
  [Guid("94067f15-fdf5-463e-b18d-4766c20e0f9c")]
  public class BimAssembliesCommand : Command
  {
    public BimAssembliesCommand() { Instance = this; }
    public static BimAssembliesCommand Instance { get; private set; }
    public override string EnglishName => "BimAssemblies";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var dialog = new AssemblyManagerDialog(doc, model);
      // ShowModal keeps this dependency-free; Rhino.UI also offers ShowSemiModal
      // if you would rather leave the viewports live while the editor is open.
      bool changed = dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
      if (!changed) return Result.Cancel;

      uint undoRecord = doc.BeginUndoRecord("BimAssemblies");
      try
      {
        List<string> warnings;
        WallBaker.RebuildAll(doc, model, out warnings);
        CommandUtil.ReportWarnings(warnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      return Result.Success;
    }
  }

  /// <summary>Regenerates every wall from its parameters. The repair command:
  /// run it after a file has been edited by something that does not know about
  /// Stratum, or after a units change.</summary>
  [Guid("a1f4d985-b1a0-4ecc-a0e9-db06ab0c0418")]
  public class BimRebuildCommand : Command
  {
    public BimRebuildCommand() { Instance = this; }
    public static BimRebuildCommand Instance { get; private set; }
    public override string EnglishName => "BimRebuild";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);
      if (model.Walls.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: no walls in this document.");
        return Result.Nothing;
      }

      uint undoRecord = doc.BeginUndoRecord("BimRebuild");
      int count;
      List<string> warnings;
      try
      {
        using (StratumDoc.Suspend())
        {
          foreach (var orphan in WallBaker.FindOrphans(doc, model))
            doc.Objects.Delete(orphan, true);
        }
        count = WallBaker.RebuildAll(doc, model, out warnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      CommandUtil.ReportWarnings(warnings);
      RhinoApp.WriteLine("Stratum: {0} of {1} walls rebuilt.", count, model.Walls.Count);
      StratumDoc.RaiseModelChanged(doc);
      doc.Views.Redraw();
      return Result.Success;
    }
  }

  /// <summary>Exports a wall and material takeoff as CSV - areas, R-values,
  /// weights and costs, per wall and per layer.</summary>
  [Guid("c666162c-293a-4c2e-bbc9-7a2481b3204c")]
  public class BimScheduleCommand : Command
  {
    public BimScheduleCommand() { Instance = this; }
    public static BimScheduleCommand Instance { get; private set; }
    public override string EnglishName => "BimSchedule";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);
      if (model.Walls.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: nothing to schedule.");
        return Result.Nothing;
      }

      var dialog = new Rhino.UI.SaveFileDialog
      {
        Title = "Save Stratum wall schedule",
        DefaultExt = "csv",
        Filter = "CSV files (*.csv)|*.csv||",
        FileName = "wall-schedule.csv"
      };
      if (!dialog.ShowSaveDialog()) return Result.Cancel;

      var text = BuildCsv(doc, model);

      try
      {
        System.IO.File.WriteAllText(dialog.FileName, text, Encoding.UTF8);
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Stratum: could not write the schedule - " + ex.Message);
        return Result.Failure;
      }

      RhinoApp.WriteLine("Stratum: schedule written to " + dialog.FileName);
      return Result.Success;
    }

    static string BuildCsv(RhinoDoc doc, BimModel model)
    {
      var inv = CultureInfo.InvariantCulture;
      var sb = new StringBuilder();

      sb.AppendLine("WALL SCHEDULE");
      sb.AppendLine("Wall,Level,Type,Description,Length (ft),Height (ft),Gross (sf),Openings (sf),Net (sf)," +
                    "Thickness (in),R nominal,R effective,Weight (psf),Cost/sf,Total cost");

      double toInch = Units.ModelToInch(doc);
      double grandTotal = 0.0;

      foreach (var wall in model.Walls)
      {
        var assembly = model.AssemblyOf(wall);
        if (assembly == null) continue;

        double lengthFt = wall.Length * toInch / 12.0;
        double heightFt = wall.Height * toInch / 12.0;
        double gross = lengthFt * heightFt;
        double openings = wall.Openings.Sum(o =>
          ((o.WidthIn + o.RoughClearanceIn) * (o.HeightIn + o.RoughClearanceIn)) / 144.0);
        double net = Math.Max(0.0, gross - openings);
        double costPerSf = assembly.CostPerSqFt(model.Catalog);
        double total = net * costPerSf;
        grandTotal += total;

        sb.AppendLine(string.Join(",", new[]
        {
          Csv(string.IsNullOrEmpty(wall.Name) ? wall.GroupName : wall.Name),
          Csv(model.FindLevel(wall.LevelId)?.Name ?? ""),
          Csv(assembly.Code),
          Csv(assembly.Name),
          lengthFt.ToString("0.00", inv),
          heightFt.ToString("0.00", inv),
          gross.ToString("0.0", inv),
          openings.ToString("0.0", inv),
          net.ToString("0.0", inv),
          assembly.TotalThicknessIn.ToString("0.000", inv),
          assembly.RValue(model.Catalog).ToString("0.0", inv),
          assembly.EffectiveRValue(model.Catalog).ToString("0.0", inv),
          assembly.WeightPsf(model.Catalog).ToString("0.0", inv),
          costPerSf.ToString("0.00", inv),
          total.ToString("0.00", inv)
        }));
      }

      sb.AppendLine();
      sb.AppendLine("TOTAL," + grandTotal.ToString("0.00", inv));
      sb.AppendLine();

      sb.AppendLine("MATERIAL TAKEOFF");
      sb.AppendLine("Wall,Type,Layer,Function,Product,Manufacturer,SKU,Thickness (in)," +
                    "Area (sf),R,Cost/sf,Cost");

      foreach (var wall in model.Walls)
      {
        var assembly = model.AssemblyOf(wall);
        if (assembly == null) continue;

        double lengthFt = wall.Length * toInch / 12.0;
        double heightFt = wall.Height * toInch / 12.0;
        double openings = wall.Openings.Sum(o =>
          ((o.WidthIn + o.RoughClearanceIn) * (o.HeightIn + o.RoughClearanceIn)) / 144.0);
        double net = Math.Max(0.0, lengthFt * heightFt - openings);

        for (int i = 0; i < assembly.Layers.Count; i++)
        {
          var layer = assembly.Layers[i];
          if (!layer.Enabled) continue;

          var product = model.Catalog.FindProduct(layer.ProductId);
          double costPerSf = product == null ? 0.0 : product.CostPerSqFt * (1.0 + product.WasteFactor);

          sb.AppendLine(string.Join(",", new[]
          {
            Csv(string.IsNullOrEmpty(wall.Name) ? wall.GroupName : wall.Name),
            Csv(assembly.Code),
            (i + 1).ToString(inv),
            Csv(layer.Function.ToString()),
            Csv(product?.Name ?? layer.ProductName),
            Csv(product?.Manufacturer ?? ""),
            Csv(product?.Sku ?? ""),
            layer.ThicknessIn.ToString("0.000", inv),
            net.ToString("0.0", inv),
            (product?.RValueAt(layer.ThicknessIn) ?? 0.0).ToString("0.00", inv),
            costPerSf.ToString("0.00", inv),
            (net * costPerSf).ToString("0.00", inv)
          }));
        }
      }

      return sb.ToString();
    }

    static string Csv(string value)
    {
      if (string.IsNullOrEmpty(value)) return "";
      return value.Contains(",") || value.Contains("\"")
        ? "\"" + value.Replace("\"", "\"\"") + "\""
        : value;
    }
  }

  /// <summary>Lists what the plug-in can do.</summary>
  [Guid("fd59497f-0c4c-4777-a41b-275cacd93227")]
  public class BimHelpCommand : Command
  {
    public BimHelpCommand() { Instance = this; }
    public static BimHelpCommand Instance { get; private set; }
    public override string EnglishName => "BimHelp";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      RhinoApp.WriteLine("Stratum BIM commands");
      RhinoApp.WriteLine("  BimWall             Draw layered walls with a live 3-D preview");
      RhinoApp.WriteLine("  BimOpening          Insert a window, door or opening into a wall");
      RhinoApp.WriteLine("  BimWallEdit         Retype, re-height or re-justify selected walls");
      RhinoApp.WriteLine("  BimWallTop          Set a wall to a height, a level, or cap it to a roof surface");
      RhinoApp.WriteLine("  BimLevels           Add, rename, move or delete building levels");
      RhinoApp.WriteLine("  BimWallProperties   Open the BIM Wall panel (layer stack editor)");
      RhinoApp.WriteLine("  BimAssemblies       Edit wall types and the product catalog");
      RhinoApp.WriteLine("  BimLibrary          Save or load the shared office library");
      RhinoApp.WriteLine("  BimSectionStyles    Apply or remove the per-material section hatching");
      RhinoApp.WriteLine("  BimSchedule         Export the wall and material takeoff as CSV");
      RhinoApp.WriteLine("  BimRebuild          Regenerate every wall from its parameters");
      RhinoApp.WriteLine("");
      RhinoApp.WriteLine("Selection: click a wall to select the whole system,");
      RhinoApp.WriteLine("           Ctrl+Shift+click to select one layer inside it.");
      RhinoApp.WriteLine("Sections:  add a Rhino clipping plane - every layer cuts with its own");
      RhinoApp.WriteLine("           hatch, poche fill and cut-line weight automatically.");
      return Result.Success;
    }
  }
}
