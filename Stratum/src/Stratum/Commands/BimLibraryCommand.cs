using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;

namespace Stratum.Commands
{
  /// <summary>
  /// Moves wall types and products between this document and the shared office
  /// library. Documents stay self-contained - the library is how a standard
  /// gets from one project to the next, not a live dependency.
  /// </summary>
  [Guid("3e4095ad-add3-4af4-ac1a-b29fa275118c")]
  public class BimLibraryCommand : Command
  {
    public BimLibraryCommand() { Instance = this; }
    public static BimLibraryCommand Instance { get; private set; }
    public override string EnglishName => "BimLibrary";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var go = new GetOption();
      go.SetCommandPrompt("Shared library");
      int optSave = go.AddOption("SaveToLibrary");
      int optLoad = go.AddOption("LoadFromLibrary");
      int optReset = go.AddOption("RestoreDefaults");
      int optWhere = go.AddOption("ShowLocation");

      if (go.Get() != Rhino.Input.GetResult.Option) return Result.Cancel;
      var option = go.Option();
      if (option == null) return Result.Cancel;

      if (option.Index == optWhere)
      {
        RhinoApp.WriteLine("Stratum library: " + AssemblyCatalog.DefaultLibraryPath);
        return Result.Success;
      }

      if (option.Index == optSave)
      {
        string error;
        if (!model.Catalog.SaveToFile(AssemblyCatalog.DefaultLibraryPath, out error))
        {
          RhinoApp.WriteLine("Stratum: could not save the library - " + error);
          return Result.Failure;
        }
        RhinoApp.WriteLine("Stratum: {0} wall types and {1} products saved to {2}",
          model.Catalog.Assemblies.Count, model.Catalog.Products.Count,
          AssemblyCatalog.DefaultLibraryPath);
        return Result.Success;
      }

      if (option.Index == optLoad)
      {
        string error;
        var library = AssemblyCatalog.LoadFromFile(AssemblyCatalog.DefaultLibraryPath, out error);
        if (library == null)
        {
          RhinoApp.WriteLine("Stratum: could not load the library - " + error);
          return Result.Failure;
        }

        int before = model.Catalog.Assemblies.Count;
        model.Catalog.Merge(library, overwrite: true);
        model.Catalog.SyncLayerNames();

        List<string> warnings;
        Documents.WallBaker.RebuildAll(doc, model, out warnings);
        CommandUtil.ReportWarnings(warnings);

        RhinoApp.WriteLine("Stratum: library merged ({0} wall types before, {1} after).",
                           before, model.Catalog.Assemblies.Count);
        StratumDoc.RaiseModelChanged(doc);
        return Result.Success;
      }

      if (option.Index == optReset)
      {
        model.Catalog.Merge(CatalogDefaults.Create(), overwrite: true);
        model.Catalog.SyncLayerNames();

        List<string> warnings;
        Documents.WallBaker.RebuildAll(doc, model, out warnings);
        CommandUtil.ReportWarnings(warnings);

        RhinoApp.WriteLine("Stratum: the built-in wall types and products have been restored.");
        StratumDoc.RaiseModelChanged(doc);
        return Result.Success;
      }

      return Result.Cancel;
    }
  }
}
