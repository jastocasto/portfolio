using System;
using Rhino;
using Rhino.Geometry;
using Stratum.Core;

namespace Stratum.Modeling
{
  /// <summary>
  /// Builds the solid that removes one layer's share of an opening.
  ///
  /// This is where "the opening resolves intelligently" actually happens: the
  /// cutter is not one box through the whole wall. Each layer gets its own
  /// cutter, sized by that layer's jamb, head and sill resolution, so gypsum can
  /// wrap the reveal, sheathing can butt the rough opening, cladding can return
  /// to the frame and a membrane can be held back for sealant - all in one wall,
  /// all visible on any section cut you take through it.
  /// </summary>
  public static class OpeningCutter
  {
    public static Brep BuildCutter(RhinoDoc doc, WallDefinition wall, LayeredAssembly assembly,
                                   AssemblyLayer layer, Opening opening,
                                   Curve workingCurve, double startExtension,
                                   LayerRange range, double wallHeight, double tol)
    {
      if (opening == null || layer == null || workingCurve == null) return null;

      double i2m = Units.InchToModel(doc);
      double bleed = Math.Max(tol * 10.0, 0.01 * i2m);

      // ---- resolutions -----------------------------------------------------
      double jambInset, headInset, sillInset;
      if (opening.OverrideResolutions)
      {
        jambInset = layer.EdgeInset(opening.JambOverride, layer.JambReturnIn);
        headInset = layer.EdgeInset(opening.HeadOverride, layer.HeadReturnIn);
        sillInset = layer.EdgeInset(opening.SillOverride, layer.SillReturnIn);
      }
      else
      {
        jambInset = layer.JambInset;
        headInset = layer.HeadInset;
        sillInset = layer.SillInset;
      }

      // ---- rough opening in model units ------------------------------------
      double roWidth = Math.Max(0.0, opening.WidthIn + opening.RoughClearanceIn) * i2m;
      double roHeight = Math.Max(0.0, opening.HeightIn + opening.RoughClearanceIn) * i2m;
      if (roWidth <= tol || roHeight <= tol) return null;

      double curveLength = workingCurve.GetLength();
      double centre = opening.StationAlongWall + startExtension;

      double s0 = centre - roWidth * 0.5 + jambInset * i2m;
      double s1 = centre + roWidth * 0.5 - jambInset * i2m;

      // A layer whose wrap is deeper than half the opening simply never gets cut.
      if (s1 - s0 <= tol) return null;

      s0 = Math.Max(0.0, Math.Min(curveLength, s0));
      s1 = Math.Max(0.0, Math.Min(curveLength, s1));
      if (s1 - s0 <= tol) return null;

      // ---- vertical extent -------------------------------------------------
      double sillZ = wall.BaseElevation + opening.SillHeightEffectiveIn * i2m;
      double headZ = sillZ + roHeight;

      double zLo, zHi;
      if (opening.Kind == OpeningKind.Door)
      {
        // Doors run to the floor; overshoot downward so the cut is always clean.
        zLo = wall.BaseElevation - bleed;
        zHi = wall.BaseElevation + roHeight - headInset * i2m;
      }
      else
      {
        zLo = sillZ + sillInset * i2m;
        zHi = headZ - headInset * i2m;
      }

      if (zHi - zLo <= tol) return null;

      // Openings that reach the top of the wall must overshoot it.
      double wallTop = wall.BaseElevation + wallHeight;
      if (zHi >= wallTop - bleed) zHi = wallTop + bleed;
      if (zLo <= wall.BaseElevation + bleed) zLo = wall.BaseElevation - bleed;

      // ---- the slice of baseline the opening sits on -----------------------
      double t0, t1;
      if (!workingCurve.LengthParameter(s0, out t0)) return null;
      if (!workingCurve.LengthParameter(s1, out t1)) return null;

      var slice = workingCurve.Trim(t0, t1);
      if (slice == null || !slice.IsValid) return null;

      // ---- cutter solid ----------------------------------------------------
      // Bleed only through the thickness, never along the jambs: the jamb faces
      // are real building faces and must land exactly where the resolution says.
      return WallBuilder.MakeLayerSolid(slice,
                                        range.Low - bleed,
                                        range.High + bleed,
                                        zLo,
                                        zHi - zLo,
                                        tol);
    }

    /// <summary>Rough-opening outline in 3-D, used for previews and for tagging.</summary>
    public static Curve RoughOpeningOutline(RhinoDoc doc, WallDefinition wall, Opening opening,
                                            double exteriorOffset)
    {
      if (wall?.Baseline == null || opening == null) return null;

      double i2m = Units.InchToModel(doc);
      var baseline = WallSolver.Flatten(wall.Baseline, wall.BaseElevation);

      Plane frame;
      if (!WallSolver.FrameAtStation(baseline, opening.StationAlongWall, out frame)) return null;

      double w = Math.Max(0.0, opening.WidthIn + opening.RoughClearanceIn) * i2m;
      double h = Math.Max(0.0, opening.HeightIn + opening.RoughClearanceIn) * i2m;
      double sill = opening.SillHeightEffectiveIn * i2m;

      var origin = frame.Origin + frame.YAxis * exteriorOffset + Vector3d.ZAxis * sill;
      var plane = new Plane(origin, frame.XAxis, Vector3d.ZAxis);

      var rect = new Rectangle3d(plane, new Interval(-w * 0.5, w * 0.5), new Interval(0, h));
      return rect.ToNurbsCurve();
    }
  }
}
