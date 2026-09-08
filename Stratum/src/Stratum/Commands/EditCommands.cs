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
      if (model.Walls.Count == 0 && model.Slabs.Count == 0 && model.Roofs.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: nothing to rebuild in this document.");
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

        List<string> slabWarnings;
        count += SlabBaker.RebuildAll(doc, model, out slabWarnings);
        warnings.AddRange(slabWarnings);
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      CommandUtil.ReportWarnings(warnings);
      RhinoApp.WriteLine("Stratum: {0} of {1} elements rebuilt.",
                         count, model.Walls.Count + model.Slabs.Count + model.Roofs.Count);
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

      AppendElementSchedule(sb, doc, model);

      AppendOpeningSchedule(sb, doc, model, OpeningKind.Window, "WINDOW SCHEDULE");
      AppendOpeningSchedule(sb, doc, model, OpeningKind.Door, "DOOR SCHEDULE");

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

    /// <summary>Floors and roofs, by type, with area, thickness, R and cost.</summary>
    static void AppendElementSchedule(StringBuilder sb, RhinoDoc doc, BimModel model)
    {
      if (model.Slabs.Count == 0 && model.Roofs.Count == 0) return;

      var inv = CultureInfo.InvariantCulture;
      double toInch = Units.ModelToInch(doc);
      double sqFtPerModel = (toInch * toInch) / 144.0;

      sb.AppendLine("FLOOR AND ROOF SCHEDULE");
      sb.AppendLine("Element,Kind,Type,Description,Level,Area (sf),Thickness (in)," +
                    "R nominal,R effective,Weight (psf),Cost/sf,Total cost");

      double total = 0.0;

      foreach (var slab in model.Slabs)
      {
        var assembly = model.Catalog.FindAssembly(slab.AssemblyId);
        if (assembly == null) continue;

        double area = slab.PlanArea * sqFtPerModel;
        double costPerSf = assembly.CostPerSqFt(model.Catalog);
        total += area * costPerSf;

        sb.AppendLine(string.Join(",", new[]
        {
          Csv(slab.DisplayName), "Floor", Csv(assembly.Code), Csv(assembly.Name),
          Csv(model.FindLevel(slab.LevelId)?.Name ?? ""),
          area.ToString("0.0", inv),
          assembly.TotalThicknessIn.ToString("0.000", inv),
          assembly.RValue(model.Catalog).ToString("0.0", inv),
          assembly.EffectiveRValue(model.Catalog).ToString("0.0", inv),
          assembly.WeightPsf(model.Catalog).ToString("0.0", inv),
          costPerSf.ToString("0.00", inv),
          (area * costPerSf).ToString("0.00", inv)
        }));
      }

      foreach (var roof in model.Roofs)
      {
        var assembly = model.Catalog.FindAssembly(roof.AssemblyId);
        if (assembly == null) continue;

        // A roof's area is the sloped surface area, which is what gets bought.
        double area = RoofArea(doc, roof) * sqFtPerModel;
        double costPerSf = assembly.CostPerSqFt(model.Catalog);
        total += area * costPerSf;

        sb.AppendLine(string.Join(",", new[]
        {
          Csv(roof.DisplayName), "Roof", Csv(assembly.Code), Csv(assembly.Name), "",
          area.ToString("0.0", inv),
          assembly.TotalThicknessIn.ToString("0.000", inv),
          assembly.RValue(model.Catalog).ToString("0.0", inv),
          assembly.EffectiveRValue(model.Catalog).ToString("0.0", inv),
          assembly.WeightPsf(model.Catalog).ToString("0.0", inv),
          costPerSf.ToString("0.00", inv),
          (area * costPerSf).ToString("0.00", inv)
        }));
      }

      sb.AppendLine("TOTAL," + total.ToString("0.00", inv));
      sb.AppendLine();
    }

    static double RoofArea(RhinoDoc doc, RoofDefinition roof)
    {
      var obj = doc.Objects.FindId(roof.SurfaceObjectId);
      var brep = obj?.Geometry as Rhino.Geometry.Brep;
      if (brep != null) return brep.GetArea();

      var surface = obj?.Geometry as Rhino.Geometry.Surface;
      if (surface != null) return Rhino.Geometry.AreaMassProperties.Compute(surface)?.Area ?? 0.0;

      return 0.0;
    }

    /// <summary>
    /// A real window or door schedule: one row per type, counted, with the sizes,
    /// rough openings, performance numbers and cost a permit set asks for.
    /// </summary>
    static void AppendOpeningSchedule(StringBuilder sb, RhinoDoc doc, BimModel model,
                                      OpeningKind kind, string title)
    {
      var inv = CultureInfo.InvariantCulture;

      var instances = model.Walls
        .SelectMany(w => w.Openings.Select(o => new { Wall = w, Opening = o }))
        .Where(x => x.Opening.Kind == kind)
        .ToList();

      if (instances.Count == 0) return;

      sb.AppendLine(title);
      sb.AppendLine("Mark,Type,Qty,Unit width,Unit height,R.O. width,R.O. height,Head height," +
                    "Level,Operation,Glazing,U-factor,SHGC,VT,Manufacturer,Model,Unit cost,Total cost");

      double toInch = Units.ModelToInch(doc);
      double scheduleTotal = 0.0;

      // Group by type where one exists, and fall back to grouping loose openings by
      // their size so an untyped model still schedules sensibly.
      var groups = instances
        .GroupBy(x => x.Opening.UnitId != Guid.Empty
          ? x.Opening.UnitId.ToString()
          : string.Format(inv, "loose:{0}x{1}", x.Opening.WidthIn, x.Opening.HeightIn))
        .OrderBy(g => g.First().Opening.Name);

      foreach (var group in groups)
      {
        var first = group.First();
        var opening = first.Opening;
        var unit = model.Catalog.FindUnit(opening.UnitId);

        double width = unit?.WidthIn ?? opening.WidthIn;
        double height = unit?.HeightIn ?? opening.HeightIn;
        double clearance = unit?.RoughClearanceIn ?? opening.RoughClearanceIn;

        int quantity = group.Count();
        double unitCost = unit?.Cost ?? 0.0;
        scheduleTotal += unitCost * quantity;

        // Head height above the level the host wall sits on.
        double headAboveLevel = opening.SillHeightEffectiveIn + height + clearance;
        var level = model.FindLevel(first.Wall.LevelId);

        sb.AppendLine(string.Join(",", new[]
        {
          Csv(MarkOf(group.Select(g => g.Opening))),
          Csv(unit?.Name ?? "untyped"),
          quantity.ToString(inv),
          Csv(Units.FeetInchesShort(width)),
          Csv(Units.FeetInchesShort(height)),
          Csv(Units.FeetInchesShort(width + clearance)),
          Csv(Units.FeetInchesShort(height + clearance)),
          Csv(Units.FeetInchesShort(headAboveLevel)),
          Csv(level?.Name ?? ""),
          Csv(unit?.Operation ?? ""),
          Csv(unit?.Glazing ?? ""),
          (unit?.UFactor ?? 0.0).ToString("0.00", inv),
          (unit?.SHGC ?? 0.0).ToString("0.00", inv),
          (unit?.VisibleTransmittance ?? 0.0).ToString("0.00", inv),
          Csv(unit?.Manufacturer ?? ""),
          Csv(unit?.Model ?? ""),
          unitCost.ToString("0.00", inv),
          (unitCost * quantity).ToString("0.00", inv)
        }));
      }

      sb.AppendLine("TOTAL," + scheduleTotal.ToString("0.00", inv));
      sb.AppendLine();
    }

    /// <summary>The marks in a group, collapsed: "W-01, W-04, W-07".</summary>
    static string MarkOf(IEnumerable<Opening> openings)
    {
      var marks = openings.Select(o => o.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                          .Distinct().OrderBy(n => n).ToList();
      if (marks.Count == 0) return "";
      if (marks.Count <= 4) return string.Join(", ", marks);
      return string.Join(", ", marks.Take(3)) + " +" + (marks.Count - 3);
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
      RhinoApp.WriteLine("  BimFloor            Floor a room by clicking in it, or from picked curves");
      RhinoApp.WriteLine("  BimRoof             Build a layered roof off a surface you drew");
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
