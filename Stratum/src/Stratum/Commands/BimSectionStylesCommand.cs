using System;
using System.Collections.Generic;
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
  /// Applies, refreshes or removes the per-material section styles.
  ///
  /// Walls get their section style when they are baked, so this is normally only
  /// needed after editing the section settings in the catalog, or after opening a
  /// file made before section styles existed. It also exists so that anyone who
  /// wants plain shaded cuts can turn the whole thing off in one command rather
  /// than object by object.
  /// </summary>
  [Guid("952fb5d5-487b-4d27-8f63-058a2bebd77a")]
  public class BimSectionStylesCommand : Command
  {
    public BimSectionStylesCommand() { Instance = this; }
    public static BimSectionStylesCommand Instance { get; private set; }

    public override string EnglishName => "BimSectionStyles";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);

      var go = new GetOption();
      go.SetCommandPrompt("Section styles for Stratum walls");
      int optApply = go.AddOption("Apply");
      int optRemove = go.AddOption("Remove");
      int optPatterns = go.AddOption("InstallPatternsOnly");

      if (go.Get() != Rhino.Input.GetResult.Option) return Result.Cancel;
      var option = go.Option();
      if (option == null) return Result.Cancel;

      if (option.Index == optPatterns)
      {
        SectionPatterns.EnsureAll(doc, model.Catalog);
        RhinoApp.WriteLine("Stratum: construction hatch patterns installed. " +
                           "They are now available to any hatch or section in this file.");
        return Result.Success;
      }

      uint undoRecord = doc.BeginUndoRecord("BimSectionStyles");
      try
      {
        if (option.Index == optApply)
        {
          SectionPatterns.EnsureAll(doc, model.Catalog);

          List<string> warnings;
          int count = WallBaker.RebuildAll(doc, model, out warnings);
          CommandUtil.ReportWarnings(warnings);

          RhinoApp.WriteLine("Stratum: section styles applied to {0} walls. " +
                             "Add a clipping plane to see the cut.", count);
          return Result.Success;
        }

        if (option.Index == optRemove)
        {
          int cleared = 0;
          using (StratumDoc.Suspend())
          {
            foreach (var wall in model.Walls)
              foreach (var id in wall.LayerObjectIds)
              {
                var obj = doc.Objects.FindId(id);
                if (obj == null) continue;

                var attributes = obj.Attributes.Duplicate();
                attributes.RemoveCustomSectionStyle();
                attributes.SectionAttributesSource = ObjectSectionAttributesSource.FromLayer;

                if (doc.Objects.ModifyAttributes(obj, attributes, true)) cleared++;
              }
          }

          doc.Views.Redraw();
          RhinoApp.WriteLine("Stratum: section styles removed from {0} layer solids. " +
                             "Run this command again with Apply to put them back.", cleared);
          return Result.Success;
        }
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      return Result.Cancel;
    }
  }
}
