# Stratum BIM — parametric layered wall assemblies for Rhino 8

A real Rhino plug-in (`.rhp`), built against RhinoCommon 8, that models walls the
way they are actually built: **one solid per material layer**, held together as
one wall system, driven by a product catalog that carries thickness, R-value,
weight, vapour permeance and cost.

The point of modelling this way is that a section cut anywhere through the model
*is* the detail. If the layers do not resolve at a window jamb, you see it in the
model before it becomes an RFI on site.

---

## What it does

**Draw** — `BimWall` draws walls by picking points, with the full layered
assembly rendered in 3-D under the cursor as you drag. Each span between two
picks becomes its own wall, so corners mitre and each run can be edited on its
own. `FromCurves` builds walls along linework you already have.

**Justify from the core** — the wall's reference line defaults to the **centre
line of the structural core**. That is the whole trick behind the layer editing:
when you make the plywood thicker, the core does not move — the exterior layers
push outward and the interior layers push inward from where they already were.
The other five justifications (either face, either core face, wall centre) are a
dropdown away when you need to hold a face to a property line instead.

**Select like a Rhino user already does** — the layers of a wall are one Rhino
group, so:

| action | selects |
|---|---|
| click | the whole wall system |
| Ctrl+Shift+click | one individual layer inside it |
| click a row in the BIM Wall panel | highlights that layer in the viewport |

Nothing new to learn — it is Rhino's own group behaviour.

**Specify** — the **BIM Wall** panel opens with a to-scale section of the wall
type: every layer in its own colour, exterior on the left, the structural core
called out, and the reference line drawn where the justification actually puts
it. Click a band in the section, or a row in the table below it, and that layer
highlights in the viewport.

Under the section is the layer stack: core, function, product, thickness, R and
$/sf per layer, with the assembly totals beneath (nominal R, framing-corrected
effective R, U, psf, $/sf, and the cost of the actual selected wall area). Swap
the product in a row and the wall regenerates: new thickness, new R, new cost,
core unmoved.

Because wall types are shared, the panel says so: it tells you how many walls a
layer edit is about to change, and warns you outright if your selection spans
more than one type. Lengths accept what a builder would type — `8`, `8'-0"`,
`96"`, `2400mm`, `5-1/2` — and if it can't read one, it says why instead of
silently snapping back.

**Open** — `BimOpening` inserts a window, door or plain opening, hosted on the
wall by station along its baseline. Every layer is cut **individually**, by that
layer's own rule at the jamb, head and sill:

| resolution | what it models |
|---|---|
| `Butt` | layer stops flush with the rough opening |
| `Wrap` | layer turns into the reveal and lines the jamb / head / sill |
| `ReturnToFrame` | layer runs in only as far as the frame it dies into |
| `HoldBack` | layer is held back for a sealant joint or drainage |
| `Continuous` | layer is not cut at all (air barrier taped across, etc.) |

So gypsum can wrap the reveal, sheathing can butt the R.O., brick can return to
the frame and the WRB can be held back — in the same wall, all visible on any
section you cut.

**Cost and performance** — `BimSchedule` writes a CSV with a wall schedule
(length, height, gross/opening/net area, thickness, nominal and effective R,
weight, $/sf, total) and a material takeoff line for every layer of every wall,
with manufacturer and SKU.

**Share** — `BimLibrary` saves the wall types and product catalog to a shared
office library and merges it into other projects. Documents always stay
self-contained: the whole catalog is written into the `.3dm`.

---

## Commands

| command | what it does |
|---|---|
| `BimWall` | draw layered walls with a live 3-D preview |
| `BimOpening` | insert a window, door or opening |
| `BimWallEdit` | retype, re-height, re-justify or flip selected walls |
| `BimWallProperties` | open the BIM Wall panel |
| `BimAssemblies` | edit wall types and the product catalog |
| `BimLibrary` | save / load / reset the shared library |
| `BimSchedule` | export the schedule and takeoff as CSV |
| `BimRebuild` | regenerate every wall from its parameters (the repair command) |
| `BimHelp` | list the commands |

A toolbar with all of them ships as `Stratum.rui` beside the `.rhp`.

---

## The wall types it ships with

| code | type | thickness | effective R |
|---|---|---|---|
| W1 | 2x6 wood frame, 2" exterior polyiso, rainscreen fiber cement | ~10-1/4" | ~R-28 |
| W2 | 2x4 interior partition, 5/8" both sides | 4-3/4" | — |
| W3 | 1-hour rated steel stud partition, 2 layers 5/8" Type X each side | 6-1/8" | — |
| W4 | Brick veneer over 2x6 frame, 2" drained cavity | ~14" | ~R-26 |
| W5 | 8" CMU, continuous XPS, hat channel, 5/8" gypsum | ~11-1/8" | ~R-14 |
| W6 | 8" concrete foundation, 2" XPS, furring, 5/8" gypsum | ~12-1/8" | ~R-13 |

…built from about 45 products with real thicknesses, published R-values,
densities, permeance and placeholder installed costs. Replace the costs with your
supplier pricing in `BimAssemblies` → **Products**, then `BimLibrary` →
**SaveToLibrary** and every future project starts from your numbers.

---

## How it is put together

```
src/Stratum/
  StratumPlugIn.cs        plug-in entry point, document read/write, panel registration
  Core/                   the data model - products, layers, assemblies, walls, openings
    MaterialProduct.cs      a real orderable product: thickness, R, cost, density, perm
    AssemblyLayer.cs        one layer + how it terminates at an opening
    WallAssembly.cs         the ordered stack; R, cost, weight, and the justification maths
    WallDefinition.cs       a wall's parameters: baseline, height, type, justification
    Opening.cs              a hosted window / door / opening
    AssemblyCatalog.cs      the catalog, and its on-disk library format
    CatalogDefaults.cs      the seed catalog above
    BimModel.cs             everything Stratum knows about one document
    Ark.cs                  version-proof serialisation into 3dm archives
    Units.cs                inches <-> model units, and builder-shorthand parsing
  Modeling/               geometry, with no dependency on the document
    WallSolver.cs           justification -> signed offsets; reliable planar offsetting
    WallBuilder.cs          layer solids, mitres, opening cuts
    OpeningCutter.cs        per-layer cutters from the jamb/head/sill rules
    WallJoiner.cs           mitred corners where two walls meet
  Documents/              the bridge to the Rhino document
    WallBaker.cs            the only code that writes geometry; groups, layers, user text
    StratumDoc.cs           per-document model + Move/Copy/Delete/Undo handling
    DocKeys.cs              the user-string keys stamped on every solid
  Commands/               BimWall, BimOpening, BimWallEdit, BimSchedule, ...
  Ui/                     the Eto.Forms panel and catalog editor
```

Two design decisions carry most of the weight:

1. **Geometry is output, never input.** The `.3dm` stores the parameters; the
   solids are regenerated from them. That is why changing a product cannot leave
   the model half-updated.
2. **The document is the source of truth for identity.** Every solid carries
   plain 3dm user text (`Stratum:Wall`, `Stratum:Product`, `Stratum:RValue`, …),
   so the link survives Copy, Paste, Export, Import and WorkSession — and so
   anything downstream, including Grasshopper and other applications, can read
   the data off the geometry without this plug-in.

---

## What has actually been verified

This was written without Rhino and without a .NET compiler, so rather than
assert that it works, here is precisely what was checked and how:

**Every API call was verified against the real assemblies.** The RhinoCommon
NuGet package was downloaded and its `RhinoCommon.dll`, `Rhino.UI.dll` and
`Eto.dll` parsed directly from their .NET metadata. All 192 type/member/overload
references the plug-in makes were checked against that index — parameter types,
parameter names used as named arguments, which parameters have defaults, and
which base class each member is inherited from. All 192 resolve. This was run
against both RhinoCommon 8.19 (the pinned version) and 8.34 (current), with
identical results.

That check found two genuine build-breakers, now fixed:

1. The originally pinned RhinoCommon 8.0.23304.9001 ships **no `lib/net7.0`
   asset** — it is net48-only, as is every package before 8.19. The `net7.0-windows`
   target could only have resolved it through NuGet's asset fallback. Pinned to
   8.19.25132.1001, the first release with a real .NET 7 target.
2. The explicit `Eto.Forms` PackageReference was **wrong**. The RhinoCommon
   package ships `Eto.dll` itself (2.9 in 8.19, 2.11 in 8.34), so a separate
   reference resolves a different Eto identity. Removed.

**The core arithmetic is unit-tested.** `tests/wall_math_test.py` is a
line-for-line port of the justification maths and the unit parsing, with 55
assertions — run it with `python3 tests/wall_math_test.py`. It proves the claim
the whole plug-in rests on: with `CoreCenter` justification the core's centre
line sits exactly on the baseline for every combination of layer thicknesses;
thickening the exterior sheathing by 1/4" moves the exterior face out by exactly
1/4" and leaves the interior face untouched; thickening the interior gypsum does
the mirror image; layer ranges stay contiguous and sum to the total thickness
under all six justifications, flipped and unflipped.

**What is still unverified:** it has not been compiled, and it has not been run
in Rhino. Static verification cannot catch a type-inference failure, a
generic-constraint problem, or a wrong assumption about *behaviour* — whether
`Curve.Offset` returns the pieces I expect on a particular polyline, whether a
boolean difference succeeds on a given wall, whether the panel lays out well at
a narrow dock width. Expect to spend a session shaking those out.

## Status, honestly

Everything above is implemented. What is **not** in this version, and would be
the next work:

- **T-junctions and 3-way corners** are left square. Only clean two-wall corners
  are mitred, because a T needs a rule about which layers run through — that is a
  detailing decision and deserves an explicit UI rather than a guess.
- **Window and door units** are openings only. The rough opening, its per-layer
  resolutions and the schedule data are modelled; the frame, sash and glazing
  geometry is not.
- **Sloped and gabled wall tops** — walls are prismatic between two elevations.
- **Roofs, floors and their intersections with walls.**
- **Section annotation** — the model sections correctly with Rhino's own clipping
  planes and `Make2D`, and the per-material layers mean hatching is controllable,
  but Stratum does not yet generate a tagged detail.
- **Per-opening resolution overrides** exist in the data model (`Opening.OverrideResolutions`)
  but have no UI yet; resolutions are edited per layer in the catalog editor,
  which is where they belong for a wall type, but there is no way to say "this
  one window returns differently" without the command line.
- **Editing a shared wall type rebuilds every wall using it,** synchronously. On
  a few dozen walls that is instant; on several hundred it will pause. Worth
  making incremental before the model gets big.
