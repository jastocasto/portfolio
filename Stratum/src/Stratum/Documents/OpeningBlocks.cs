using System;
using System.Collections.Generic;
using System.Globalization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Stratum.Core;
using Stratum.Modeling;

namespace Stratum.Documents
{
  /// <summary>
  /// Places your own window and door geometry into the openings Stratum cuts.
  ///
  /// Stratum does not model frames, sashes or glazing — you already have that
  /// geometry. Instead an opening unit names a Rhino block; if a block definition
  /// with that name exists in the document, every opening of that type gets an
  /// instance of it, positioned and oriented in the rough opening and grouped with
  /// the wall so it moves when the wall moves.
  ///
  /// No block of that name, no geometry, no error — the hole is still cut and the
  /// unit still schedules. So the library can be dropped in later and the walls
  /// rebuilt.
  ///
  /// **Block convention.** Draw the unit in the world XY plane as if standing
  /// outside looking at it:
  ///   X — width, centred on the origin
  ///   Y — height, origin at the rough sill
  ///   Z — depth back through the wall, positive toward the interior
  /// Stratum lands that origin on the exterior face of the wall at the centre of
  /// the rough opening, shifted inward by the unit's BlockInset.
  /// </summary>
  public static class OpeningBlocks
  {
    /// <summary>
    /// Inserts the block for every opening in a wall that names one. Returns the
    /// ids of the instances added, for the wall to keep alongside its layer solids.
    /// </summary>
    public static List<Guid> Place(RhinoDoc doc, BimModel model, WallDefinition wall,
                                   int groupIndex, List<string> warnings)
    {
      var placed = new List<Guid>();
      if (doc == null || model == null || wall?.Baseline == null) return placed;

      var assembly = model.AssemblyOf(wall);
      if (assembly == null) return placed;

      double i2m = Units.InchToModel(doc);
      var baseline = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);
      if (baseline == null) return placed;

      double exteriorOffset, interiorOffset;
      WallSolver.FaceOffsets(assembly, wall.Justification, wall.Flipped, i2m,
                             out exteriorOffset, out interiorOffset);

      foreach (var opening in wall.Openings)
      {
        var unit = model.Catalog.FindUnit(opening.UnitId);
        if (unit == null || string.IsNullOrWhiteSpace(unit.BlockName)) continue;

        InstanceDefinition definition;
        try { definition = doc.InstanceDefinitions.Find(unit.BlockName); }
        catch { definition = null; }

        if (definition == null)
        {
          warnings.Add("No block named '" + unit.BlockName + "' in this document, so " +
                       opening.Name + " was cut but left empty. Add the block and rebuild.");
          continue;
        }

        Plane target;
        if (!FramePlane(doc, wall, opening, unit, baseline, exteriorOffset, i2m, out target))
          continue;

        var transform = Transform.PlaneToPlane(Plane.WorldXY, target);

        var attributes = new ObjectAttributes
        {
          Name = opening.Name + " · " + unit.Name
        };
        if (groupIndex >= 0) attributes.AddToGroup(groupIndex);

        attributes.SetUserString(DocKeys.Wall, wall.Id.ToString());
        attributes.SetUserString(DocKeys.Opening, opening.Id.ToString());
        attributes.SetUserString(DocKeys.OpeningMark, opening.Name);
        attributes.SetUserString(DocKeys.OpeningUnit, unit.Id.ToString());
        attributes.SetUserString(DocKeys.ProductName, unit.Name);
        attributes.SetUserString(DocKeys.Manufacturer, unit.Manufacturer);
        attributes.SetUserString(DocKeys.Sku, string.IsNullOrEmpty(unit.Model) ? unit.Sku : unit.Model);

        var id = doc.Objects.AddInstanceObject(definition.Index, transform, attributes);
        if (id != Guid.Empty) placed.Add(id);
      }

      return placed;
    }

    /// <summary>
    /// The plane the block is landed on: origin at the centre of the rough opening
    /// on the exterior face at sill level, X along the wall, Y up, Z into the wall.
    /// </summary>
    static bool FramePlane(RhinoDoc doc, WallDefinition wall, Opening opening, OpeningUnit unit,
                           Curve baseline, double exteriorOffset, double inchToModel,
                           out Plane plane)
    {
      plane = Plane.Unset;

      Plane frame;
      if (!WallSolver.FrameAtStation(baseline, opening.StationAlongWall, out frame)) return false;

      // frame.YAxis is the left of the curve, which is the exterior unless flipped.
      var outward = wall.Flipped ? -frame.YAxis : frame.YAxis;
      var inward = -outward;

      var origin = frame.Origin
                   + frame.YAxis * exteriorOffset
                   + inward * (unit.BlockInsetIn * inchToModel)
                   + Vector3d.ZAxis * (opening.SillHeightEffectiveIn * inchToModel);

      // Right-handed frame whose Z runs into the wall, so a block drawn as seen
      // from outside lands the right way round on both faces of the building.
      var yAxis = Vector3d.ZAxis;
      var xAxis = Vector3d.CrossProduct(yAxis, inward);
      if (!xAxis.Unitize()) return false;

      plane = new Plane(origin, xAxis, yAxis);
      return plane.IsValid;
    }

    /// <summary>Removes the instances placed for a wall. Called before a rebuild,
    /// alongside the layer solids.</summary>
    public static void Erase(RhinoDoc doc, IEnumerable<Guid> ids)
    {
      if (doc == null || ids == null) return;
      foreach (var id in ids)
      {
        var obj = doc.Objects.FindId(id);
        if (obj != null) doc.Objects.Delete(obj, true);
      }
    }
  }
}
