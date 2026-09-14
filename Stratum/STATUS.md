# Stratum — status

The single document for this plug-in. It replaces `OPEN-ITEMS.md` and the dated
`RHINO-RUN-*.md` files, which are gone; their content is here.

`START-HERE.md` is the walkthrough, `BUILD.md` the build detail, `README.md` the
description of intent. This file is what is **true right now**, what is not, and
what is waiting on a decision. Everything numbered here was measured in a
running Rhino, not inferred.

**The rig is `tests/rig.py`.** One call rebuilds it and runs every check below:

    exec(open(r"C:\Users\casto\NUBIM\Stratum\tests\rig.py").read())

When the script bridge is down — see §4 — go in through the command line instead,
which writes the same report to `tests/_last-run.txt`:

    -_RunPythonScript "C:\Users\casto\NUBIM\Stratum\tests\run_rig.py"

21 checks, 0 failing as of this writing. Run it after any change to `WallJoiner`,
`WallBuilder`, `OpeningCutter` or `WallSolver`. If a number moves, something moved.
The offline assertions cannot cover any of it — they run without a document, so
`inchToModel` is always 1 and no junction, notch or opening is reachable.

Last verified: **2026-09-13**, Rhino 8.36.26251.14001, plug-in
`bc93f43b-764a-41dc-8cd4-edfc72500fbf`.

---

## 1 · Where it stands

### Verified working

| | evidence |
|---|---|
| Loads; 14 commands registered | `BimAssemblies BimFloor BimHelp BimLevels BimLibrary BimOpening BimRebuild BimRoof BimSchedule BimSectionStyles BimWall BimWallEdit BimWallProperties BimWallTop` |
| Panel opens, docks, lays out | metric strip reads `241.9 mm · R-35.0` for W1 |
| Catalogue — 11 assemblies | 6 walls, 3 floors, 2 roofs |
| Unit conversion is live | `Units.InchToModel` = 25.4 in mm, 1.0 in an inches document; exercised in both |
| Core sits on the baseline | `core mid = 0.000000000 mm` at CoreCenter |
| All six justifications contiguous | span = sum = 241.859 mm, largest gap 2.84e-14 |
| **Width is parametric and side-correct** | thicken sheathing ¼" → exterior +6.350000, interior 0.000000, core 0.000000000. Thicken gypsum → the exact mirror. |
| One closed solid per layer | 8 solids for W1; volume 3,918,112,560 vs analytic 3,918,115,800 mm³ = 0.00008% |
| Full BIM record on every solid as 3dm user text | `Stratum:Wall`, `:AssemblyCode`, `:ProductName`, `:RValue`, `:CostPerSF` … readable without the plug-in installed |
| **Corner and tee both resolve, no doubled material** | 29 solids intersected pairwise in the imperial rig: 8 bbox-overlapping pairs, **0 interpenetrating, 0.0 in³** |
| **Tee ties to structure** | partition's 2x4 core stops on the host's stud face; its gypsum stops on the host's gypsum face; the host's gypsum is interrupted over exactly 3.500" |
| Layer priority defaults off `LayerFunction` | W1 → 4, 4, membrane, 3, 2, 1, membrane, 5. All 11 assemblies correct with nothing assigned by hand. |
| Editing a wall type reaches its neighbours | `WallJoiner.Touching` returns the W2 tee when given the W1 walls, and leaves an unrelated W2 wall alone |
| **Corners are corner boards, not mitres** | every layer solid has exactly **6 faces** — a mitred layer carries a diagonal face and would have 7+. No diagonal anywhere in the model. |
| **Which wall runs past can be flipped** | `BimCornerFlip`: pick near a corner, both walls and their neighbours rebuild. Default RUN wins — siding to 246.260, stud to 242.750. Flipped, CORNER wins — its siding to 6.260, its stud to 2.750, and RUN now butts at 245.947 / 237.250. An exact mirror, still 0 in³ interpenetrating, still every solid 6-faced. |
| **Openings hold at a corner** | three windows on a 20 ft run, one 24 in from the corner: cut where they should be, and the corner return intact beside them — probes read solid at x=235.5, 242 and 245, where the window's reach would otherwise have removed them |
| An opening that does not fit is refused, loudly | a 36 in window centred 6 in from the wall end warns *runs 12-1/4 in past the end of wall RUN and was not cut* and cuts nothing |
| A full-height opening splits a layer and keeps both halves | 5 of 8 layers become two solids (0.000–131.750 and 168.250–…); the other 3 are `Continuous` at the sill and correctly stay whole. 25 solids, none open, 0 in³ interpenetrating. |
| The flip survives the file | `CornerFlips` round-trips through `ToDictionary`/`FromDictionary` unchanged; the key is order-independent, so it does not matter which wall the solver reaches first |
| **A regression rig exists** | `tests/rig.py` builds the rig and runs **21 checks** in one call. All pass as of 2026-09-13. |
| **Geometry files onto the office layer standard** | W1's eight solids land on `Env-Wall-Wood`, `Env-Barr-Battens`, `Env-Barr-WRB`, `Env-Barr-IzoExt`, `Struct-Shth-OSB`, `Struct-Wall-WdStud`, `Env-Barr-Air`, `Int-Wall-Plaster`. All 57 catalogue layers across the 11 assemblies resolve to a layer that exists. |
| **Baking creates no layers** | 129 layers before a rebuild, 129 after. Checked by the rig. |
| The fallback is exact | rename one standard layer away and only that material drops to the `Stratum::` tree; the other seven stay put. Rename it back and the tree is empty again. |
| The winner runs past, the loser butts | RUN (drawn first) runs each layer to the far face of its counterpart — siding 246.260, stud 242.750, gypsum 237.238. CORNER butts each layer on the near face — siding 5.947, stud −2.750, gypsum −3.262. |

### Known wrong

Nothing outstanding in the wall junctions. See “Next” and “Never exercised”
below for what has simply not been tried.

### Next

Section styles, raked tops against a roof, roofs, schedules. And three- and
four-way junctions, which `WallJoiner` still leaves square by design.

### Never exercised

Openings at a junction. Section styles. Raked tops against a roof. Roofs.
Schedules. Three- and four-way junctions — `WallJoiner` leaves anything past two
walls at a point square by design, and that has never been tested.

`Height` reads `0 mm` in the panel defaults. Not investigated.

`ResDesPlugin` loads in the same Rhino session. Two window-placement systems are
live at once; decide which owns openings before either touches real work.

---

## 2 · Open, and why

### O-1 · Annotation and hatch  *(largely solved — re-measured 2026-09-13)*

**What this section used to say was out of date.** It described
`tds_imperial_template.3dm`: 65 layers, AIA-nested (`Plan::A-Wall::A-Wall`), 9
dimension styles, sharing no layers with the metric template. That file still
exists and is still like that.

But `tds_imperial_template_clean.3dm` is **not a cleaned copy of it.** Measured
off disk: 129 layers, flat and discipline-prefixed, 36 dimension styles, 33 hatch
patterns, 439 page-space objects, 14 layouts — item for item the same as
`tds_metric_template_clean.3dm`, in inches instead of millimetres. It is the
metric template converted, not the old imperial one cleaned.

So the two systems no longer diverge, and the annotation problem went with them.
Of the 36 dimension styles **20 are properly imperial** — `FeetAndInches`, text
heights 3/32", 1/8", 5/32", 3/16", 1/4" — and 15 are metric leftovers
(`1:100__2.5mm`, `TB_*mm`, `Centimeters Architectural`). Using an imperial style
in an inches document is now correct, not a workaround.

What is left is tidying: the 15 metric styles are dead weight in an imperial file
and the 20 imperial ones in a metric one. Neither does harm. Not worth doing
until something needs it.

The clean template also carried **11 empty `Stratum::` layers**, left by a session
that baked walls and saved over it. Removed 2026-09-13, with a timestamped backup
beside it; nothing else in the file was touched.

Template tolerance is 0.01". New files are set to **0.001"** — wall layer faces
land 1/64" apart and 0.01 is too coarse for the joins to be trusted.

### O-2 · Where the geometry files itself  *(answered 2026-09-13)*

**Answered: the document's layer standard wins.** Instructed plainly — use the
template's layers, add none, they are descriptive enough — and that is how the
plug-in now bakes.

`TemplateLayers` maps each material onto a layer the document already has, and
`WallBaker`/`SlabBaker` consult it before anything else. Matching is on the
product name, because `LayerFunction` alone cannot separate OSB from plywood, or
a rafter from a truss. Insulation resolves to `Env-Barr-IzoExt` or `IzoInt` by
which side of the structural core it sits on, read off the core's index rather
than `AssemblyLayer.Side`, which is advisory and not set on every catalogue entry.

**It never creates a standard layer.** If the document has no layer for a
material — a blank file, someone else's template — it returns -1 and the caller
falls back to the `Stratum::` tree exactly as before. So the plug-in still works
anywhere, and in a tds document the walls land where the drawings are controlled
from. Both halves are checked by the rig.

Three judgement calls, agreed rather than derived: fiber cement siding files on
`Env-Wall-Wood` (right by profile and trim, wrong by material); CMU on
`Struct-Wall-Core` rather than `Struct-Wall-Concrete`; under-slab polythene on
`Env-Barr-WaterproofInt` rather than `Env-Barr-Air` — it is a ground-damp
barrier, not an air barrier.

Interior gypsum goes to `Int-Wall-Plaster`, **not** `Struct-Shth-Gyp`: that layer
is gypsum *sheathing*, an exterior structural board. Gypsum on the underside of a
floor or roof goes to `Int-Ceiling-Finish`.

### O-3 · Which drawing engine survives  *(PRODUCTION-SYSTEM.md Q10)*

**Answered by R-2 below, at least for construction drawings.** Annotation has to
live on Rhino layouts and stay live, and a headless pipeline cannot maintain that.
So the CD drawing engine is the plug-in. D4 — no Rhino at runtime — still stands
for the web tools and House Anatomy; it does not stand for the sheets.
Worth writing back into PRODUCTION-SYSTEM.md properly.

### R-1 · Suppress the drawn seam between matching materials  *(blocked on O-3)*

Confirmed as a requirement: where two layers of the **same material** meet face
to face, the 2D output must not draw a line between them.

Priority does not fix this. Priority decides which layer runs through and which
butts; it still leaves two solids touching, and any naive section draws the
shared edge. Revit suppresses it by comparing materials at the join.

The rule, per candidate edge in a plan or section — drop it only when all three
hold:

1. the edge is shared by exactly two layer solids, and
2. they are coplanar across it within tolerance, and
3. the two layers resolve to the same material.

It belongs at draw time, not in the modelling: the solids must stay separate for
take-off and per-layer editing.

Condition 3 needs a material **family** on `MaterialProduct` — coarser than
`ProductId`, finer than `LayerFunction` — so that 5/8" Type X butting 1/2"
regular still reads as gypsum meeting gypsum, which is exactly House Anatomy's
Gap AT. That field does not exist and should not be added until there is
something to consume it.

A thickness change within a family still draws a line at the step, correctly:
the step's faces are not coplanar, so rule 2 excludes it.

**Blocked because there is no 2D stage to put it in.** `SectionPatterns.cs`
assigns hatch patterns to layers; that is all "section" currently means in
Stratum. There is no `Make2D`, no silhouette extraction, no plan generation.

### The wider one · eight definitions of a wall assembly

Counted in the Tellurian repo on 2026-09-13:

`waller/src/data/materials.ts` + `templates.ts` · `tds-materials/materials.v1.json` ·
`assemblylab/public/data/materials.v1.json` · `tds-materials/models/schema.py` +
`seed_assemblies.py` + `ui/assemblies.py` · `house-anatomy/assemblies/wall.js` ·
`modeller/tools/styles.json` · `roofer/src/data/roofTemplates.ts` ·
`Stratum/Core/CatalogDefaults.cs`

Plus `Seashell_Web_BIM_v1` still in the repo root, an `archive/`, a `_retired/`,
and three Proton name-clash copies of `takeoff.py` beside the real one.

Two things worth knowing. The two `materials.v1.json` are **byte-identical**
(sha1 `59361d71bf88`, 13 materials) — a clean copy, not drift. And
`tools/build-materials.mjs` opens by calling itself *"Single source of truth for
the material library"*, for a different library entirely — the woods-and-stones
field guide. The phrase is already in the repo, attached to the wrong file.

Stratum's catalogue is the most developed of the eight — 11 assemblies,
measured, and the only one that produces geometry — but it is C# inside a Rhino
plug-in, which is the worst possible home for something a website and a Python
takeoff must read. Unification means extracting it to a neutral data file that
everything, Stratum included, reads from.

**Sequence deliberately: after corners and openings, not before.** The modelling
is still telling us what the schema needs; extracting a schema that is about to
change means doing it twice.

### R-2 · Live sheet annotation — what was measured  *(2026-09-14)*

Before designing anything, the four load-bearing assumptions were tested in a
running Rhino. Three held, one did not, and one turned out to be a trap.

**Object ids do not survive a rebuild. 0 of 25.** `WallBaker` erases and re-adds
every solid, so every id changes even when nothing about the wall did — the
geometry signature was byte-identical across the rebuild, so this is pure
identity churn. Any annotation addressed to a layer solid breaks the first time
its wall is touched, which is exactly when it must not.

**The wall's own GUID is stable.** `Stratum:Wall` is the same before and after.
Model-level identity survives; only the Rhino objects churn.

**A (wall, layerIndex) key is not unique.** 6 of the rig's 25 solids share one,
because a notched or opened layer is several pieces. Anything keyed that way has
to answer "which piece", and the piece count changes when an opening does.

**Rhino's own text field works, and the quoting matters:**

```
%<UserText("2ad1f0c1-…","Stratum:ProductName")>%      resolves
%<UserText(2ad1f0c1-…,"Stratum:ProductName")>%        gives ####
```

The id must be quoted; unquoted parses but never resolves. It reads **attribute**
user text, which is what `WallBaker` already writes, and several fields compose in
one string — `… @ … in` came back as `Wood stud 2x6 @ 5.5 in`. Verify with
`Rhino.Runtime.TextFields.TryFormat(s, doc)`; `TextObject.DisplayText` and
`PlainText` both return the raw formula and tell you nothing.

> **The trap.** `obj.Attributes` hands back a copy with **no user strings in it**.
> Set one key on that copy, commit it with `ModifyAttributes`, and every user
> string on the object is silently wiped — the BIM record included. Two ways that
> do work, both preserving the object id: build a complete fresh `ObjectAttributes`
> carrying *every* key and pass that, or call
> `obj.Attributes.SetUserString(k, v)` followed by `obj.CommitChanges()`.

So an object's identity can be made permanent while its data changes underneath
it. That is the hinge the design turns on, and it is now measured rather than
assumed.

### R-2 · Annotation lives on the sheets, and stays live  *(stated 2026-09-13)*

The requirement, in full:

> Annotations and dimensions go on the **sheets in the Rhino file** — the layouts,
> not model space. As the model changes they update. As **section locations move**
> they update. And they can be overridden by hand: text and leader position.

Three consequences, and the third is the hard one.

**It settles O-3.** Live annotation on a Rhino layout can only be maintained by
something running inside Rhino. That is the plug-in.

**Annotation is derived, not authored.** A dimension is not a drawn object that
happens to sit near a wall; it is a *view* of a fact in the model — this layer's
thickness, this opening's head height above this datum — projected through a
section definition onto a sheet. Move the section, and the same fact projects
somewhere else. So the sheet holds generated geometry, and the generator has to
be re-runnable.

**But regeneration must not destroy hand work.** This is the whole point of the
original ask — the complaint about web tooling was precisely that there was no
manual override. So every annotation needs:

* a **stable identity** tied to what it annotates, not to where it sits: wall id
  plus layer index plus which edge, opening id plus which dimension. The wall ids
  are already stamped on every solid as 3dm user text — `Stratum:Wall`,
  `Stratum:LayerIndex` — so the hook exists.
* an **override record**: moved by hand, text replaced by hand, suppressed. A
  regeneration rewrites what has no override and leaves the rest exactly alone.
* a way to see which is which, because an annotation that silently stopped
  tracking the model is worse than one that was never generated.

None of this is built. It is the largest remaining piece of the original four
asks and the only one still at zero.

---

## 3 · Log

### 2026-09-13 · first live run, and a correction

Eleven checks passed. The twelfth — junctions — was **first reported as failing,
and that report was wrong.**

The measurement said 54 of 54 layer pairs clashing, 0.0320 m³ interpenetrating.
The cause was not the joiner. Two of the three test walls had silently inherited
`BaseElevation = 1980.000026787379` from the active construction plane, so they
floated above the third and merely passed through it in a 720 mm band. They
never met, so `WallJoiner` correctly declined to join them.

Redrawn with an explicit base of 0: **0 clashes, corner and tee both resolve.**

> **The lesson, which generalises.** A clash count cannot distinguish "these
> meet and the joiner failed" from "these never met". Compare the Z extents of
> every wall before trusting the number.

### 2026-09-13 · two defects found and fixed

**D-B · A notch discarded everything past it.** A layer was one `Brep`. A notch
where another wall tees in, or an opening running a layer's full height, splits
it into two disjoint solids — and `Brep.JoinBreps` cannot join disjoint solids,
so `JoinBreps(...).FirstOrDefault()` returned one and dropped the other.
Measured: a 6000 mm wall teed at 2930 mm lost the gypsum from its notched face
for the remaining 3070 mm. The same mistake sat in four places — the notch, the
opening cut, the joint trim, and the roof rake, where a gable legitimately
leaves a layer in two pieces.

Fixed: `WallLayerSolid.Brep` → `List<Brep> Solids`, every cut maps over the
list, and a `Difference` helper keeps every piece and reports failure separately
so a failed boolean warns instead of losing the layer.

Verified in inches: a 20'-0" run's interior gypsum returns as two solids,
0.000→118.250 and 121.750→237.238, gap exactly **3.500"** — the partition's 2x4
core.

**Base elevation came from the construction plane.** `BimWall` read the CPlane's
Z straight into the wall. Rhino carries the CPlane over from whatever was drawn
last, so walls were built at arbitrary heights that looked right in plan, were
wrong in section, and never met each other. This is what produced the false
finding above. The CPlane now only selects the level; the level supplies the
elevation. `BaseElevation` at the prompt still takes anything off-level.

**Priority added.** `LayerFunction` already encoded the ordering, so the default
falls out of it — Structure 1, Sheathing 2, Insulation/AirGap 3,
Furring/Cladding 4, Finish 5, membranes larger than any real layer so they never
win and never wrap. `Ark.Num` returns its fallback for a missing key, so older
documents read 0 and take the default: no schema bump, no migration.

**Neighbour rebuild.** `WallPanel` rebuilt only `WallsUsing(assembly)`. A
junction is solved from *both* walls' layer stacks, so changing one wall type
left a partition tee'd into it on its old stopping plane — floating clear or
buried — with nothing said. `WallJoiner.Touching` now returns the edited walls
plus everything whose baseline meets one of them.

### 2026-09-13 · D-A closed — the corner is a corner board

`WallJoiner.Corner` replaces the bisector mitre. One wall wins, its layer runs
past, the other wall's matching layer butts into the back of it. The rule, whole:

> A layer travelling toward the corner is halted by the first of the other
> wall's layers whose offsets overlap its own and whose priority is equal or
> stronger. Winning means running past that band to its **far** face; losing
> means butting into its **near** face.

That is the entire algorithm. No layer is named anywhere in it, and it produces,
for two identical W1 walls: a corner post where the studs meet, siding that
wraps with the other wall's siding butting behind it, and gypsum that wraps at
the inside corner.

`WallJoint` gained `Dictionary<int, Plane> LayerPlanes` and
`PlaneFor(layerIndex, isCore)`. The mitre planes stay as the fallback for any
layer the per-layer pass cannot place, so a corner is never left doubled up;
`JointKind.Miter` is kept for the odd-angled corner that still wants one.

The winner is the earlier wall in the model by default — stable, and arbitrary
in the way Revit's join order is arbitrary. **`BimCornerFlip` overrides it**:
pick near a corner and the two walls swap roles, with the choice stored on the
model as an order-independent key and carried in the 3dm. Every layer flips
together; letting the siding wrap one way and the studs the other is not a
corner anybody builds.

Measured after deploying: 21 solids, every one 6-faced, **0 bbox-overlapping
pairs and 0.0 in³ interpenetrating**. The tee's notch survives unchanged — the
run's gypsum still returns as two pieces with a 3.500" gap.

### 2026-09-13 · Openings at a corner — one defect, found by testing

The interaction to worry about was an opening near a junction cutting layers a
joint has already cut. It does that correctly. What it did **not** do correctly
was stop.

A wall's baseline is run past both ends so joints have material to cut back, and
at a corner that extension **is** the corner return — the siding that turns the
corner, the stud that makes the post. `OpeningCutter` clamped the rough opening
to the *working* curve, extensions included, so a window near the end cut
straight through the return. Measured before the fix: a 36 in window centred
6 in from the corner left the wall empty at x=242 and x=245, where the stud and
the siding should have been. Every solid was still closed, so nothing complained.

Two changes. `OpeningCutter` now clamps to the wall that was drawn, not the
working curve. And `WallBuilder` checks each opening against the wall's own
length first and refuses the ones that do not fit, naming the opening and the
overshoot, because a silently narrowed window is worse than none:

> Opening 'W-OVER' runs 12-1/4 in past the end of wall 'RUN' and was not cut.
> Move it along the wall or narrow it.

After: the two windows that fit cut where they should, the corner return is
intact beside them, and a full-height opening splits five of the eight layers
into two solids with both halves kept — the D-B fix and the opening cutter
composing correctly, which was the other thing worth checking.

### 2026-09-13 · The layer standard takes over, and the rig had been lying

A new project file was wanted on `tds_imperial_template_clean`, with the rule:
**add no layers, they are descriptive enough, put geometry inside them.** That
rule settles O-2, and the work to honour it is above. What it also did was expose
two things nobody had measured.

**The clean imperial template is the metric one in inches** — see O-1. Four years
of assuming those were separate systems, and they are the same system.

**The rig only worked because the document was empty.** `solids(doc)` returned
*every object in the document*. In a blank file that is exactly the rig; opened on
a real template it swept up 439 page-space objects across 13 layouts and reported
464 solids and 47,639 bounding-box overlaps before dying on
`DetailView.IsSolid`. It now selects on the `Stratum:Wall` user string, which is
on the layer solids and on nothing else — and had to stop selecting on layer
name too, since the solids no longer live on a `Stratum::` layer at all.

> **The lesson.** A test that runs only in an empty document is testing the
> document as much as the code. The template was the first real document the rig
> had ever been pointed at, and it failed on contact.

Two checks added, so this cannot regress quietly: every solid sits on the
standard rather than a `Stratum::` layer, and a rebuild creates no layers. Both
skip themselves in a document without the standard, where the fallback is the
correct answer.

### 2026-09-13 · Rhino environment, changed permanently

- `ApplicationSettings.FileSettings.TemplateFile` **cleared**. It pointed at
  `tds_metric_template.3dm`, so every new document arrived with 129 layers and
  ~1,100 objects. Rhino now opens blank. Revert in Tools ▸ Options ▸ Files.
- Five plug-ins set to load silently via `PlugIn.SetLoadProtection(id, true)` —
  Adobe Illustrator Export, Walkabout, 3Dconnexion 3D Mouse, 3D Studio Export,
  Displacement. Their modal prompts stop Rhino reaching a usable state.

### Doc drift found

`README.md` calls W1 "~10-1/4"". It is 9.522". Regenerate that table from
`CatalogDefaults` rather than maintaining it by hand.

---

## 4 · Traps

**Rhino loads the path it registered first.** Registered here:

```
HKCU\Software\McNeel\Rhinoceros\8.0\Plug-Ins\bc93f43b-…\PlugIn
  -> src\Stratum\bin\Release\net7.0-windows\Stratum.rhp
```

A successful Release build *is* the install. Building into `dist\` and expecting
Rhino to notice does nothing.

**Rhino holds the .rhp open**, so the build's copy step fails while it runs and
a green build silently leaves last week's code loaded. Always check the .rhp's
timestamp actually moved.

**A watcher spawned from inside Rhino cannot deploy.** The MCP router runs Rhino
inside a **job object**, so anything Rhino starts is killed the moment Rhino
exits — which is precisely when the deploy needs to run. Three attempts failed
silently this way. Use Task Scheduler, which is outside the job:

```
schtasks /create /tn StratumDeploy ^
  /tr "cmd /c C:\Users\casto\NUBIM\Stratum\wait-and-deploy.cmd" ^
  /sc once /st HH:MM /it /f
```

`dotnet` is **not on PATH** in that context — call it absolutely
(`C:\Program Files\dotnet\dotnet.exe`).

**Never run `_New` or `_-Open` over the MCP bridge.** Both close the document
the slot is bound to; the router prunes the slot and the session is lost. Build
documents in place instead — set units, then `open_doc` to import a template's
layers.

**`run_python` over the MCP bridge can stop answering while everything else on
the bridge still works.** Seen 2026-09-13: every `run_python` and `run_csharp`
call returned *"An error occurred invoking 'run_python'"*, including
`print(1+1)`, while `run_command`, `list_objects` and `get_viewport_image` were
all fine — so Rhino itself was healthy and only the script host was wedged.

The way in is the command line, which is not the same channel:

```
-_RunPythonScript "C:\Users\casto\NUBIM\Stratum\tests\run_rig.py"
```

`run_rig.py` execs `rig.py`, captures everything it prints, and writes it to
`tests/_last-run.txt` — necessary because `run_command` returns only what Rhino
echoes. Its `#! python 3` first line is what makes Rhino 8 hand the file to
CPython; without it the file goes to IronPython 2 and `rig.py`'s f-strings do
not parse.

**`-_RunPythonScript`** with the dash prefix takes the path as an argument and
opens no dialog. Without the dash it opens a file browser and the session hangs.

**`-t:Compile`** checks the code without the copy step, so it works while Rhino
is running.
