using System;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Ui;

namespace Stratum
{
  /// <summary>
  /// Stratum BIM - parametric layered wall assemblies for Rhino 8.
  ///
  /// Loading contract with Rhino:
  ///  * the assembly's [Guid] attribute is the plug-in id;
  ///  * the file extension must be .rhp;
  ///  * LoadAtStartup is required for a plug-in that owns a toolbar and a panel,
  ///    otherwise the buttons exist before the code that answers them does;
  ///  * document data is read and written through the archive overrides below, so
  ///    a .3dm carries its own walls, wall types and product catalog and opens
  ///    correctly on a machine that has never seen this project before.
  /// </summary>
  public class StratumPlugIn : PlugIn
  {
    public StratumPlugIn()
    {
      Instance = this;
    }

    public static StratumPlugIn Instance { get; private set; }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
      try
      {
        StratumDoc.HookEvents();

        Rhino.UI.Panels.RegisterPanel(
          this,
          typeof(WallPanel),
          "BIM Wall",
          (System.Drawing.Icon)null);

        RhinoApp.WriteLine("Stratum BIM {0} loaded. Type BimWall to draw, BimHelp for the command list.",
                           Version);
      }
      catch (Exception ex)
      {
        errorMessage = "Stratum failed to load: " + ex.Message;
        return LoadReturnCode.ErrorShowDialog;
      }

      return LoadReturnCode.Success;
    }

    protected override void OnShutdown()
    {
      StratumDoc.UnhookEvents();
      base.OnShutdown();
    }

    // ---- document data -----------------------------------------------------

    protected override bool ShouldCallWriteDocument(FileWriteOptions options)
    {
      if (options.WriteGeometryOnly || options.WriteSelectedObjectsOnly) return false;
      var doc = RhinoDoc.ActiveDoc;
      if (doc == null) return false;
      var model = StratumDoc.Get(doc);
      return model != null && (model.Walls.Count > 0 || model.Catalog.Assemblies.Count > 0);
    }

    protected override void WriteDocument(RhinoDoc doc, BinaryArchiveWriter archive, FileWriteOptions options)
    {
      var model = StratumDoc.Get(doc);
      archive.Write3dmChunkVersion(1, 0);
      archive.WriteDictionary(model.ToDictionary());
    }

    protected override void ReadDocument(RhinoDoc doc, BinaryArchiveReader archive, FileReadOptions options)
    {
      int major, minor;
      archive.Read3dmChunkVersion(out major, out minor);
      if (major != 1)
      {
        RhinoApp.WriteLine("Stratum: this file was written by a newer version of the plug-in " +
                           "(data version {0}.{1}). Walls will open as plain geometry.", major, minor);
        return;
      }

      var dictionary = archive.ReadDictionary();
      var model = BimModel.FromDictionary(dictionary);

      if (options.ImportMode || options.ImportReferenceMode)
      {
        // Merge rather than replace, so importing a file does not wipe the walls
        // already in the document.
        var existing = StratumDoc.Get(doc);
        existing.Catalog.Merge(model.Catalog);
        existing.Walls.AddRange(model.Walls);
        StratumDoc.Set(doc, existing);
      }
      else
      {
        StratumDoc.Set(doc, model);
      }
    }

  }
}
