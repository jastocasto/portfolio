# Stratum — status

The single document for this plug-in. It replaces `OPEN-ITEMS.md` and the dated
`RHINO-RUN-*.md` files, which are gone; their content is here.

`START-HERE.md` is the walkthrough, `BUILD.md` the build detail, `README.md` the
description of intent. This file is what is **true right now**, what is not, and
what is waiting on a decision. Everything numbered here was measured in a
running Rhino, not inferred.

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
| The winner runs past, the loser butts | RUN (drawn first) runs each layer to the far face of its counterpart — siding 246.260, stud 242.750, gypsum 237.238. CORNER butts each layer on the near face — siding 5.947, stud −2.750, gypsum −3.262. |

### Known wrong

Nothing outstanding in the wall junctions. See “Next” and “Never exercised”
below for what has simply not been tried.

### Next

A per-junction **flip**, so the wall that runs past can be swapped. The rule is
in and correct; which wall wins is decided by draw order alone and there is no
way to override it.

### Never exercised

Openings at a junction. Section styles. Raked tops against a roof. Roofs.
Schedules. Three- and four-way junctions — `WallJoiner` leaves anything past two
walls at a point square by design, and that has never been tested.

`Height` reads `0 mm` in the panel defaults. Not investigated.

`ResDesPlugin` loads in the same Rhino session. Two window-placement systems are
live at once; decide which owns openings before either touches real work.

---

## 2 · Open, and why

### O-1 · Annotation and hatch are metric-only  *(deferred by agreement)*

`tds_metric_template.3dm` — 129 layers, flat and discipline-prefixed
(`Env-Wall-Brick`, `Struct-Wall-WdStud`, `G-Anno-Dims`, `G-Obj-Datums`), 36
dimension styles, 29 hatch patterns. This is the current system.

`tds_imperial_template.3dm` — 65 layers, AIA-nested (`Plan::A-Wall::A-Wall`),
9 dimension styles, 13 hatch patterns, with model leftovers still in it.
**The two share no layers at all.**

So an inches document on the real layer system has to take the metric
template's layers, and its annotation is then wrong: text height 2.5 reads as
2.5 inches, hatch scales are out by 25.4.

Open: convert the styles to imperial, or keep imperial files model-only.
Deferred deliberately — a later session must not silently pick one.

**Do not edit either template.** Both originals are untouched. Cleaned copies
(model geometry stripped, every layer unlocked and visible, page space and all
settings intact) sit beside them as `tds_metric_template_clean.3dm` and
`tds_imperial_template_clean.3dm`.

### O-2 · Stratum's layer tree sits beside the template's, not inside it

Wall solids bake onto `Stratum::Walls::W1::01 Fiber cement lap siding, …`.
The template's wall layers are `Struct-Wall-WdStud`, `Env-Wall-Brick`,
`Struct-Shth-OSB`. Both live in the same document and neither knows about the
other. Layers drive line weight, colour and print control in sheets, so this
decides how drawings are controlled. Settle it before there is a project's worth
of geometry on the wrong branch.

### O-3 · Which drawing engine survives  *(PRODUCTION-SYSTEM.md Q10)*

Gates R-1. Nothing to build until it is answered.

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

The winner is the earlier wall in the model — stable, and arbitrary in the way
Revit's join order is arbitrary.

Measured after deploying: 21 solids, every one 6-faced, **0 bbox-overlapping
pairs and 0.0 in³ interpenetrating**. The tee's notch survives unchanged — the
run's gypsum still returns as two pieces with a 3.500" gap.

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

**`-t:Compile`** checks the code without the copy step, so it works while Rhino
is running.
