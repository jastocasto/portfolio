# -*- coding: utf-8 -*-
"""
Stratum test rig - build it, measure it, prove it still works.

WHY THIS FILE EXISTS
--------------------
Every defect Stratum has had was found by building real walls in a real document
and measuring them, not by the offline assertions. Those 211 assertions run
WITHOUT a RhinoDoc, so `inchToModel` is always 1 and no junction, no notch and no
opening is reachable by them. This file is the other half: a rig that exists in a
live document, and a set of checks whose expected numbers were measured on
2026-09-13 after the junction work.

Run it after any change to WallJoiner, WallBuilder, OpeningCutter or WallSolver.
If a number here moves, something moved.

HOW TO RUN
----------
From the Rhino script editor, or over the MCP bridge in one call:

    exec(open(r"C:\\Users\\casto\\NUBIM\\Stratum\\tests\\rig.py").read())

That builds the rig in the current document and prints the report. Or drive the
pieces yourself:

    build(__rhino_doc__)          # datums + walls + openings, from scratch
    report(__rhino_doc__)         # the layer table and the clash test
    checks(__rhino_doc__)         # the regression checks, pass/fail
    shoot(__rhino_doc__, "name")  # colour-coded plan and iso into _shots/

TRAPS, learned the hard way - see STATUS.md section 4
-----------------------------------------------------
* NEVER run `_New` or `_-Open` over the MCP bridge. Both close the document the
  slot is bound to, the router prunes the slot, and the session is gone. This
  file builds in place for exactly that reason.
* Rhino holds Stratum.rhp open, so a code change needs the deploy dance:
  schtasks -> wait-and-deploy.cmd -> Rhino closes, builds, reopens.
* The plug-in loaded in the running Rhino is the one Rhino registered, not
  whatever you last compiled. Check the .rhp timestamp actually moved.

THE RIG
-------
Inches, tolerance 0.001. Four datums. Three walls forming one corner and one tee,
with the building interior on -Y:

    RUN     (0,0) -> (240,0)      W1 exterior, 20 ft, drawn first so it wins the corner
    CORNER  (240,0) -> (240,-144) W1 exterior, 12 ft
    TEE     (120,0) -> (120,-120) W2 partition, 10 ft, tees into RUN from inside

    W-MID   station  60   36x48 window, clear of everything
    W-NEAR  station 216   36x48 window, 24 in from the corner
    W-OVER  station 234   36x48 window that does NOT fit - must be refused
    O-FULL  station 150   36 wide, full height - must split layers and keep both
"""

import Rhino
import System
import os
from Rhino.Geometry import (Point3d, Vector3d, LineCurve, Brep, Box, Interval, Plane,
                            BoundingBox, VolumeMassProperties)
from System.Drawing import Color

from Stratum.Documents import StratumDoc, WallBaker
from Stratum.Modeling import WallJoiner
from Stratum.Core import (Level, WallDefinition, Opening, OpeningKind,
                          AssemblyJustification, WallTopMode, BimModel)

SHOTS = r"C:\Users\casto\NUBIM\Stratum\_shots"
MODEL = r"C:\Users\casto\NUBIM\Stratum\_models\stratum-rig-imperial.3dm"

DATUMS = [("FINISH FLOOR", 0.0, True), ("COUNTER", 36.0, False),
          ("HEADER", 96.0, False), ("T.O. PLATE", 120.0, False)]


# ---------------------------------------------------------------- building

def build(doc, openings=True):
    """Wipes any previous rig and builds it again. Returns the model."""
    doc.ModelUnitSystem = Rhino.UnitSystem.Inches
    doc.ModelAbsoluteTolerance = 0.001

    m = StratumDoc.Get(doc)

    for w in list(m.Walls):
        WallBaker.DeleteWall(doc, m, w)
    m.Walls.Clear()
    m.CornerFlips.Clear()

    m.Levels.Clear()
    for i, (name, elev, storey) in enumerate(DATUMS):
        level = Level()
        level.Name = name
        level.Elevation = elev
        level.SortOrder = i
        level.IsStorey = storey
        m.Levels.Add(level)

    base = m.Levels[0]
    top = m.Levels[3]
    W1 = m.Catalog.FindAssemblyByCode("W1")
    W2 = m.Catalog.FindAssemblyByCode("W2")

    def wall(name, x0, y0, x1, y1, assembly):
        w = WallDefinition()
        w.Name = name
        w.AssemblyId = assembly.Id
        w.Baseline = LineCurve(Point3d(x0, y0, 0.0), Point3d(x1, y1, 0.0))
        w.LevelId = base.Id
        w.BaseOffset = 0.0
        w.BaseElevation = 0.0
        w.TopMode = WallTopMode.ToLevel
        w.TopLevelId = top.Id
        w.TopOffset = 0.0
        w.Height = 120.0
        w.Justification = AssemblyJustification.CoreCenter
        m.Walls.Add(w)
        return w

    # Drawn in this order on purpose: RUN is first, so it wins the corner by
    # default and every expected number below assumes that.
    run = wall("RUN",     0.0,   0.0, 240.0,    0.0, W1)
    wall("CORNER",      240.0,   0.0, 240.0, -144.0, W1)
    wall("TEE",         120.0,   0.0, 120.0, -120.0, W2)

    if openings:
        def window(host, name, station, width=36.0, height=48.0, sill=36.0,
                   kind=OpeningKind.Window):
            o = Opening()
            o.Name = name
            o.WallId = host.Id
            o.Kind = kind
            o.StationAlongWall = station
            o.WidthIn = width
            o.HeightIn = height
            o.SillHeightIn = sill
            o.RoughClearanceIn = 0.5
            host.Openings.Add(o)
            return o

        window(run, "W-MID",  60.0)
        window(run, "W-NEAR", 216.0)
        window(run, "W-OVER", 234.0)                       # must be refused
        window(run, "O-FULL", 150.0, height=130.0, sill=0.0,
               kind=OpeningKind.Opening)                   # must split layers

    StratumDoc.Set(doc, m)
    count, warnings = WallBaker.RebuildAll(doc, m)
    StratumDoc.Set(doc, m)
    doc.Views.Redraw()
    return m, count, list(warnings)


# --------------------------------------------------------------- measuring

def solids(doc, wall_name=None):
    """Stratum's own geometry, and nothing else.

    Identified by the BIM record stamped on every layer solid, NOT by layer and
    NOT by "every object in the document". Two reasons, both learned the hard way:

    * Since wall solids file themselves onto the office layer standard
      (Env-Wall-Wood, Struct-Wall-WdStud ...) rather than a Stratum:: tree, a
      layer-name test finds nothing.
    * A real template is not an empty document. tds_imperial_template_clean
      carries 439 page-space objects across 13 layouts, so `for o in doc.Objects`
      swept up title blocks and layout detail views - which reported 464 solids,
      47,639 bounding-box overlaps, and then died on DetailView.IsSolid.

    The user text is on the solids and on nothing else, so it is the honest test.
    """
    m = StratumDoc.Get(doc)
    if wall_name is None:
        out = []
        for o in doc.Objects:
            try:
                if o.Attributes.GetUserString("Stratum:Wall"):
                    out.append(o)
            except Exception:
                pass
        return out
    w = next((x for x in m.Walls if x.Name == wall_name), None)
    if w is None:
        return []
    return [doc.Objects.FindId(i) for i in w.LayerObjectIds]


def layer_name(obj):
    return obj.Name.split(u"\u00b7")[-1].strip()


def probe(doc, x, z, half=0.4, wall_name="RUN"):
    """Material volume inside a small box at (x, z), across the whole wall.

    0.0000 means the opening is open there. About 6.09 in3 is solid W1: the probe
    is 0.8 x 0.8 in section, so it reads 0.64 x (total assembly thickness).
    """
    tol = doc.ModelAbsoluteTolerance
    box = Box(Plane.WorldXY,
              Interval(x - half, x + half),
              Interval(-8.0, 8.0),
              Interval(z - half, z + half)).ToBrep()

    total = 0.0
    for o in solids(doc, wall_name):
        try:
            pieces = Brep.CreateBooleanIntersection(o.Geometry, box, tol)
        except Exception:
            pieces = None
        if not pieces:
            continue
        for piece in pieces:
            if piece is None:
                continue
            vm = VolumeMassProperties.Compute(piece)
            if vm:
                total += vm.Volume
    return total


def clashes(doc):
    """Every pair of solids that actually interpenetrates. Should always be none.

    Bounding boxes first, then a real boolean intersection and a volume on
    anything that survives - a bbox overlap on its own proves nothing.
    """
    tol = doc.ModelAbsoluteTolerance
    objs = solids(doc)          # Stratum's solids only - see solids() for why
    boxes = [o.Geometry.GetBoundingBox(True) for o in objs]

    def overlaps(a, b, eps=1e-6):
        return (a.Min.X < b.Max.X - eps and b.Min.X < a.Max.X - eps and
                a.Min.Y < b.Max.Y - eps and b.Min.Y < a.Max.Y - eps and
                a.Min.Z < b.Max.Z - eps and b.Min.Z < a.Max.Z - eps)

    found = []
    tested = 0
    for i in range(len(objs)):
        for j in range(i + 1, len(objs)):
            if not overlaps(boxes[i], boxes[j]):
                continue
            tested += 1
            try:
                pieces = Brep.CreateBooleanIntersection(objs[i].Geometry,
                                                        objs[j].Geometry, tol)
            except Exception:
                pieces = None
            if not pieces:
                continue
            volume = 0.0
            for piece in pieces:
                if piece is None:
                    continue
                vm = VolumeMassProperties.Compute(piece)
                if vm:
                    volume += vm.Volume
            if volume > 1e-4:
                found.append((objs[i].Name, objs[j].Name, volume))
    return tested, found


def report(doc):
    m = StratumDoc.Get(doc)
    print("units %s  tolerance %g" % (doc.ModelUnitSystem, doc.ModelAbsoluteTolerance))
    print("datums: " + ", ".join("%s %g" % (l.Name, l.Elevation) for l in m.SortedLevels))
    print("")

    for corner in WallJoiner.Corners(doc, m):
        print("corner at (%.0f, %.0f): %s runs past, %s butts%s" %
              (corner.Point.X, corner.Point.Y, corner.Winner.Name, corner.Loser.Name,
               "  [flipped]" if corner.Flipped else ""))
    print("")

    for w in m.Walls:
        print("== %s ==" % w.Name)
        for o in solids(doc, w.Name):
            bb = o.Geometry.GetBoundingBox(True)
            print("   %-40s x %9.3f..%9.3f  y %9.3f..%9.3f  faces %3d  solid=%s" %
                  (layer_name(o)[:40], bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y,
                   o.Geometry.Faces.Count, o.Geometry.IsSolid))
        print("")

    tested, found = clashes(doc)
    print("solids %d | bbox-overlapping pairs %d | interpenetrating %d | volume %.4f in3"
          % (len(solids(doc)), tested, len(found), sum(f[2] for f in found)))
    for a, b, v in found:
        print("   CLASH %.4f : %s <-> %s" % (v, a[:40], b[:40]))


# ----------------------------------------------------------------- checks

def checks(doc, warnings=None):
    """The numbers measured on 2026-09-13, as regression checks.

    Each one is a defect that was real. If one fails, read the line: it says what
    the geometry is supposed to do, not just what number to expect.
    """
    results = []

    def check(name, ok, detail=""):
        results.append((bool(ok), name, detail))

    m = StratumDoc.Get(doc)
    tol = 0.002

    def span(wall_name, contains):
        """Union extent of a named layer across ALL its pieces.

        A layer is a list, not a solid: a notch or a full-height opening splits
        it. Taking piece [0] and calling it the layer is how this check first
        reported a corner failure that was not one.
        """
        found = [o.Geometry.GetBoundingBox(True)
                 for o in solids(doc, wall_name) if contains in layer_name(o)]
        if not found:
            return None
        box = found[0]
        for b in found[1:]:
            box.Union(b)
        return box

    def near(a, b):
        return a is not None and abs(a - b) <= tol

    # -- priority falls out of LayerFunction, nothing assigned by hand --------
    W1 = m.Catalog.FindAssemblyByCode("W1")
    got = [L.Priority for L in W1.Layers]
    check("W1 priorities are 4,4,membrane,3,2,1,membrane,5",
          got == [4, 4, 99, 3, 2, 1, 99, 5], str(got))

    # -- the tee notches the host and keeps BOTH pieces (defect D-B) ---------
    gyp = [o for o in solids(doc, "RUN") if "Gypsum" in layer_name(o)]
    check("RUN gypsum is in 2 pieces, notched by the tee", len(gyp) >= 2,
          "%d piece(s)" % len(gyp))
    if len(gyp) >= 2:
        gyp.sort(key=lambda o: o.Geometry.GetBoundingBox(True).Min.X)
        gap = (gyp[1].Geometry.GetBoundingBox(True).Min.X -
               gyp[0].Geometry.GetBoundingBox(True).Max.X)
        check("the notch is exactly the partition's 2x4 core, 3.500 in",
              near(gap, 3.5), "%.4f" % gap)

    # -- the corner is a corner board, not a mitre (defect D-A) --------------
    run_siding = span("RUN", "Fiber cement")
    run_stud = span("RUN", "Wood stud 2x6")
    cor_siding = span("CORNER", "Fiber cement")
    cor_stud = span("CORNER", "Wood stud 2x6")

    check("RUN siding runs past to the corner's outer face, x=246.260",
          near(run_siding.Max.X if run_siding else None, 246.260),
          "%.3f" % run_siding.Max.X if run_siding else "missing")
    check("RUN stud runs past to make the corner post, x=242.750",
          near(run_stud.Max.X if run_stud else None, 242.750),
          "%.3f" % run_stud.Max.X if run_stud else "missing")
    check("CORNER siding butts behind it, y=5.947",
          near(cor_siding.Max.Y if cor_siding else None, 5.947),
          "%.3f" % cor_siding.Max.Y if cor_siding else "missing")
    check("CORNER stud butts into RUN's stud, y=-2.750",
          near(cor_stud.Max.Y if cor_stud else None, -2.750),
          "%.3f" % cor_stud.Max.Y if cor_stud else "missing")

    # -- an opening must not eat the corner return ---------------------------
    for x, label in [(235.5, "beside the window"), (242.0, "the corner stud"),
                     (245.0, "the corner siding")]:
        v = probe(doc, x, 60.0)
        check("corner return intact at x=%.1f (%s)" % (x, label), v > 0.5,
              "%.4f in3" % v)

    # -- an opening that does not fit is refused, and says so ----------------
    if warnings is not None:
        said = any("W-OVER" in w for w in warnings)
        check("W-OVER is refused with a warning naming it", said,
              "; ".join(w for w in warnings if "W-OVER" in w) or "no warning")

    # -- the openings that do fit are cut ------------------------------------
    check("W-MID is open at mid height", probe(doc, 60.0, 60.0) < 0.01)
    check("W-MID has material outside its jamb", probe(doc, 40.5, 60.0) > 0.5)
    check("W-NEAR is open at mid height", probe(doc, 216.0, 60.0) < 0.01)
    # x=100 is clear of W-MID (41.75-78.25) and of O-FULL (131.75-168.25)
    _plain = probe(doc, 100.0, 100.0)
    check("plain wall is solid where no opening reaches", _plain > 0.5,
          "%.4f in3" % _plain)

    # -- a full-height opening splits a layer and both halves survive --------
    studs = [o for o in solids(doc, "RUN") if "Wood stud 2x6" in layer_name(o)]
    check("O-FULL splits the stud layer in two, both kept", len(studs) == 2,
          "%d piece(s)" % len(studs))

    # -- no mitre: a layer cut on the bisector carries a diagonal face, so a
    #    plain rectangular layer has exactly 6. CORNER has no openings in it, so
    #    every one of its solids must still be a box.
    cornered = solids(doc, "CORNER")
    boxed = [o for o in cornered if o.Geometry.Faces.Count == 6]
    check("every CORNER solid is a 6-faced box, so no layer is mitred",
          len(boxed) == len(cornered) and len(cornered) > 0,
          "%d of %d" % (len(boxed), len(cornered)))

    # -- nothing occupies the same space -------------------------------------
    tested, found = clashes(doc)
    # A tested count of 0 is itself the result: once the corner resolves, no two
    # layer solids even share a bounding box.
    check("no two solids interpenetrate", len(found) == 0,
          "%d pair(s) from %d bbox overlaps" % (len(found), tested))

    # -- every solid is closed ------------------------------------------------
    opened = [o for o in solids(doc) if not o.Geometry.IsSolid]
    check("every solid is closed", len(opened) == 0, "%d open" % len(opened))

    # -- the geometry files itself on the office layer standard ---------------
    #
    # Only meaningful in a document that HAS the standard. In a blank file falling
    # back to the Stratum:: tree is the correct answer, not a failure, so the check
    # asks the document first.
    has_standard = any(l.Name == "Struct-Wall-WdStud"
                       for l in doc.Layers if not l.IsDeleted)
    if has_standard:
        stray = [o for o in solids(doc)
                 if doc.Layers[o.Attributes.LayerIndex].FullPath.startswith("Stratum")]
        check("every solid is on the layer standard, not a Stratum:: layer",
              len(stray) == 0, "%d stray" % len(stray))

        made = [l.FullPath for l in doc.Layers
                if not l.IsDeleted and l.FullPath.startswith("Stratum")]
        check("baking created no layers", len(made) == 0,
              "%d created" % len(made))

    print("")
    for ok, name, detail in results:
        print("  %s  %-58s %s" % ("PASS" if ok else "FAIL", name, detail))
    failed = sum(1 for ok, _, _ in results if not ok)
    print("")
    print("  %d checks, %d failed" % (len(results), failed))
    return failed


def check_flip(doc):
    """Flip the corner and prove it mirrors, then put it back."""
    m = StratumDoc.Get(doc)
    a = next(w for w in m.Walls if w.Name == "RUN")
    b = next(w for w in m.Walls if w.Name == "CORNER")

    before = WallJoiner.WinnerOf(m, a, b).Name
    m.ToggleCornerFlip(a.Id, b.Id)
    WallBaker.RebuildMany(doc, m, WallJoiner.Touching(doc, m, [a, b]))
    StratumDoc.Set(doc, m)

    m = StratumDoc.Get(doc)
    after = WallJoiner.WinnerOf(m, a, b).Name
    cor_siding = [o for o in solids(doc, "CORNER") if "Fiber cement" in layer_name(o)]
    ran_past = cor_siding and abs(cor_siding[0].Geometry.GetBoundingBox(True).Max.Y - 6.260) < 0.002

    print("")
    print("  flip: %s was running past, now %s%s" %
          (before, after, "  (CORNER siding reaches y=6.260)" if ran_past else "  *** did not mirror ***"))

    m.ToggleCornerFlip(a.Id, b.Id)
    WallBaker.RebuildMany(doc, m, WallJoiner.Touching(doc, m, [a, b]))
    StratumDoc.Set(doc, m)
    doc.Views.Redraw()
    return bool(ran_past)


# ------------------------------------------------------------------ images

PALETTE = {
    "Fiber cement lap siding": (183, 65, 50),
    "Wood furring": (222, 140, 60),
    "Mechanically fastened WRB": (240, 205, 70),
    "Polyisocyanurate": (120, 180, 100),
    "OSB": (70, 150, 160),
    "Wood stud 2x6": (215, 175, 120),
    "Wood stud 2x4": (196, 150, 90),
    "Variable-permeance": (90, 110, 190),
    "Gypsum board, 1/2": (238, 238, 238),
    "Gypsum board, 5/8": (206, 206, 220),
}


def _colour(doc, on):
    """Layer function as colour, for reading a plan cut. Reversible."""
    saved = []
    for o in doc.Objects:
        saved.append((str(o.Id), int(o.Attributes.ColorSource),
                      o.Attributes.ObjectColor.ToArgb()))
        if not on:
            continue
        name = doc.Layers[o.Attributes.LayerIndex].Name
        rgb = next((v for k, v in PALETTE.items() if k in name), None)
        if rgb is None:
            continue
        a = o.Attributes.Duplicate()
        a.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
        a.ObjectColor = Color.FromArgb(rgb[0], rgb[1], rgb[2])
        doc.Objects.ModifyAttributes(o, a, True)
    return saved


def _restore(doc, saved):
    for sid, source, argb in saved:
        o = doc.Objects.FindId(System.Guid(sid))
        if o is None:
            continue
        a = o.Attributes.Duplicate()
        a.ColorSource = Rhino.DocObjects.ObjectColorSource(source)
        a.ObjectColor = Color.FromArgb(argb)
        doc.Objects.ModifyAttributes(o, a, True)


def shoot(doc, prefix="rig", width=2400, height=1500):
    """Colour-coded plan cuts and an iso, into _shots/.

    The plan uses an explicit ViewportInfo frustum rather than ZoomBoundingBox,
    which does not frame a flat box the way you would expect. The display mode
    has to be set after SetViewProjection and immediately before the capture, or
    the shot comes back in wireframe.
    """
    if not os.path.isdir(SHOTS):
        os.makedirs(SHOTS)

    view = next((v for v in doc.Views if v.ActiveViewport.Name == "Perspective"),
                doc.Views.ActiveView)
    doc.Views.ActiveView = view
    vp = view.ActiveViewport
    shaded = Rhino.Display.DisplayModeDescription.FindByName("Shaded")
    aspect = float(width) / float(height)
    saved = _colour(doc, True)

    def capture(name):
        view.ActiveViewport.DisplayMode = shaded
        view.Redraw()
        Rhino.RhinoApp.Wait()
        c = Rhino.Display.ViewCapture()
        c.Width = width
        c.Height = height
        c.ScaleScreenItems = False
        c.DrawAxes = False
        c.DrawGrid = False
        c.DrawGridAxes = False
        c.TransparentBackground = False
        path = os.path.join(SHOTS, "%s-%s.png" % (prefix, name))
        c.CaptureToBitmap(view).Save(path, System.Drawing.Imaging.ImageFormat.Png)
        print("   %s" % path)

    def plan(name, cx, cy, across):
        vi = Rhino.DocObjects.ViewportInfo()
        vi.SetScreenPort(0, width, 0, height, 0, 1)
        vi.IsParallelProjection = True
        vi.SetCameraLocation(Point3d(cx, cy, 600.0))
        vi.SetCameraDirection(Vector3d(0, 0, -1))
        vi.SetCameraUp(Vector3d(0, 1, 0))
        half = across / 2.0
        vi.SetFrustum(-half, half, -half / aspect, half / aspect, 1.0, 2000.0)
        vp.SetViewProjection(vi, True)
        capture(name)

    def iso(name, box, direction, lens=50.0):
        vp.SetProjection(Rhino.Display.DefinedViewportProjection.Perspective, None, False)
        vp.Camera35mmLensLength = lens
        centre = box.Center
        v = Vector3d(direction[0], direction[1], direction[2])
        v.Unitize()
        reach = box.Diagonal.Length * 1.8
        vp.SetCameraLocations(centre, Point3d(centre.X - v.X * reach,
                                              centre.Y - v.Y * reach,
                                              centre.Z - v.Z * reach))
        vp.ZoomBoundingBox(box)
        capture(name)

    try:
        plan("corner-plan", 241.0, -1.0, 34.0)
        plan("tee-plan", 120.0, 0.0, 40.0)
        plan("overall", 120.0, -60.0, 330.0)
        iso("interior", BoundingBox(Point3d(120, -30, 0), Point3d(250, 10, 120)),
            (0.55, 1.0, -0.45))
    finally:
        _restore(doc, saved)
        doc.Views.Redraw()


# -------------------------------------------------------------------- main

def main(doc=None, images=False, save=False):
    doc = doc or Rhino.RhinoDoc.ActiveDoc
    print("Stratum rig - building")
    model, count, warnings = build(doc)
    print("  %d walls rebuilt, %d warning(s)" % (count, len(set(warnings))))
    for w in sorted(set(warnings)):
        print("    WARN: %s" % w)
    print("")
    report(doc)
    failed = checks(doc, warnings)
    check_flip(doc)
    if images:
        print("")
        shoot(doc)
    if save:
        doc.SaveAs(MODEL)
        print("\n  saved %s" % MODEL)
    return failed


if __name__ == "__main__" or True:
    try:
        _doc = __rhino_doc__
    except NameError:
        _doc = Rhino.RhinoDoc.ActiveDoc
    main(_doc)
