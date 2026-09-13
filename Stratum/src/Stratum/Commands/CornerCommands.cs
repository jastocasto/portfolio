using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Stratum.Core;
using Stratum.Documents;
using Stratum.Modeling;

namespace Stratum.Commands
{
  /// <summary>
  /// Swaps which wall runs past at a corner.
  ///
  /// A corner board has a front and a back: one wall's cladding turns the corner
  /// and the other butts into the back of it. Which way round is a drawing
  /// decision - it is what decides which elevation the joint reads on - and
  /// Stratum picks it by draw order, which is stable and arbitrary. This is how
  /// you say otherwise, and the choice is saved with the model.
  ///
  /// Every layer flips together. The whole point of priority is that one rule
  /// governs the entire stack; letting the siding wrap one way and the studs the
  /// other would not be a corner anybody builds.
  /// </summary>
  [Guid("3f7a9c41-16b8-4f2e-9d5a-8c4e21b07d6f")]
  public class BimCornerFlipCommand : Command
  {
    public BimCornerFlipCommand() { Instance = this; }
    public static BimCornerFlipCommand Instance { get; private set; }
    public override string EnglishName => "BimCornerFlip";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var model = CommandUtil.Prepare(doc);
      if (model == null) return Result.Failure;

      var corners = WallJoiner.Corners(doc, model);
      if (corners.Count == 0)
      {
        RhinoApp.WriteLine("Stratum: no corners here. A corner is two wall ends landing on the same point.");
        return Result.Nothing;
      }

      RhinoApp.WriteLine("Stratum: " + corners.Count + " corner" + (corners.Count == 1 ? "" : "s") +
                         " in this model.");

      int flipped = 0;

      while (true)
      {
        var gp = new GetPoint();
        gp.SetCommandPrompt("Pick a corner to flip");
        gp.AcceptNothing(true);

        var result = gp.Get();
        if (result != GetResult.Point) break;

        var pick = gp.Point();
        var corner = Nearest(corners, pick);
        if (corner == null) break;

        double away = corner.Point.DistanceTo(pick);

        uint undoRecord = doc.BeginUndoRecord("BimCornerFlip");
        try
        {
          model.ToggleCornerFlip(corner.A.Id, corner.B.Id);

          // Both walls, and anything joined to them: a corner is solved from both
          // layer stacks, so flipping it moves geometry on each side.
          var affected = WallJoiner.Touching(doc, model, new[] { corner.A, corner.B });

          List<string> warnings;
          WallBaker.RebuildMany(doc, model, affected, out warnings);
          StratumDoc.Set(doc, model);

          foreach (var warning in warnings.Distinct().Take(3))
            RhinoApp.WriteLine("Stratum: " + warning);
        }
        finally
        {
          doc.EndUndoRecord(undoRecord);
        }

        // Recompute: the winner has changed, and so has every corner's report.
        corners = WallJoiner.Corners(doc, model);
        var now = Nearest(corners, pick);

        RhinoApp.WriteLine("Stratum: corner at " + Format(doc, corner.Point) +
                           " - " + Name(now?.Winner) + " now runs past, " +
                           Name(now?.Loser) + " butts into it" +
                           (away > 0.001 ? "  (picked " + Format(doc, away) + " away)" : ""));
        flipped++;
        doc.Views.Redraw();
      }

      if (flipped == 0) return Result.Cancel;
      return Result.Success;
    }

    static WallJoiner.CornerPair Nearest(List<WallJoiner.CornerPair> corners, Point3d pick)
    {
      WallJoiner.CornerPair best = null;
      double bestDistance = double.MaxValue;

      foreach (var corner in corners)
      {
        double d = corner.Point.DistanceTo(pick);
        if (d >= bestDistance) continue;
        bestDistance = d;
        best = corner;
      }

      return best;
    }

    static string Name(WallDefinition wall)
    {
      if (wall == null) return "?";
      if (!string.IsNullOrWhiteSpace(wall.Name)) return wall.Name;
      return "wall " + wall.Id.ToString().Substring(0, 8);
    }

    static string Format(RhinoDoc doc, Point3d p)
      => "(" + Format(doc, p.X) + ", " + Format(doc, p.Y) + ")";

    static string Format(RhinoDoc doc, double value)
      => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
  }
}
