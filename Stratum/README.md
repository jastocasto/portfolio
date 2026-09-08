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

**Junctions resolve** — where two walls meet at a corner, both run past it and are cut
back on the angle bisector so every layer mitres cleanly. Where a wall dies into the
side of another, the tee **ties to structure**: the arriving wall's core runs through
the through wall's finish layers to land on its core, its own finish layers stop at the
through wall's face, and the through wall is notched over the width of the arriving
core so the two never occupy the same space. That is what a rated or acoustic partition
actually does, and it is the difference between a section through the junction showing
something buildable and showing two walls interpenetrating.

**Levels and tops** — walls bind to a building level with an offset, so moving a level
moves everything on it. The top of a wall is a condition, not a number: an explicit
height, up to another level (a floor-to-floor change flows through), or **raked to a
surface** — pick a roof plane and the wall rises to meet it at the roof's own angle,
every layer following the slope. That last one is `BimWallTop` → `ToSurface`; edit the
roof and run `BimRebuild` to re-cut.

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

**Cut a section and it reads as a drawing** — every layer solid carries its own
Rhino 8 section style, so dropping a clipping plane anywhere in the model produces a
poché'd construction section with no setup: concrete hatched as concrete, CMU at 45°,
rigid insulation cross-hatched, batt at a dense diagonal, plywood as grain, steel
nearly solid, gypsum light — with the structural core drawn in a heavier cut line so
the structure reads first. Membranes and cavities are poché-only, because they are too
thin to hatch legibly, which is how they are drawn by hand.

Patterns are authored per inch and scaled to the document's units, so the same catalog
sections correctly in an imperial or a metric file. `BimSectionStyles` re-applies them
after a catalog edit, or strips them if you would rather have plain shaded cuts.

*Honest limitation:* Rhino hatch patterns are families of straight lines, and
RhinoCommon has no way to set a dash array programmatically, so the conventional
squiggle for batt insulation and the stipple-and-triangle for concrete are approximated
by line families at the right angle and density.

**Floors and roofs are layered too** — `BimFloor` floors a room: click inside it and the
boundary is worked out from the walls around it, or pick closed curves. `BimRoof` builds a
layered roof off a surface you drew, offsetting each material off it, so hips, valleys,
dormers and curved roofs all work because the form is your geometry rather than something
a generator had to anticipate.

The same offset maths drives all three — a floor's layers are the wall solver about a
different axis — so the justification behaviour proven for walls is the behaviour floors
and roofs get. Both default to the reference landing on **top of the structural core**:
top of joists, top of slab, top of rafters, which is what gets set out. Point `BimRoof`
and a wall's `ToSurface` cap at the *same* surface and the two agree by construction.

**Windows and doors are types, and take your geometry** — openings are instances of a
unit in the catalog, so re-typing one window resizes every instance and the schedule
counts them properly. Stratum does not model frames or sashes: a unit can name a **Rhino
block**, and if a block of that name exists it is placed and oriented in every rough
opening of that type and grouped with the wall. No block, no geometry, no error — the
hole is still cut and scheduled, so a library can be dropped in later and the walls
rebuilt.

Draw the block in the world XY plane as seen from outside: X is width centred on the
origin, Y is height from the rough sill, Z runs back through the wall.

**Cost and performance** — `BimSchedule` writes a CSV with a wall schedule
(level, length, height, gross/opening/net area, thickness, nominal and effective R,
weight, $/sf, total), a window schedule and a door schedule (mark, type, quantity, unit
and rough-opening sizes, head height, level, operation, glazing, U-factor, SHGC, VT,
manufacturer, model and cost), and a material takeoff line for every layer of every wall.

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
| `BimWallTop` | set a height, build to a level, or rake to a roof surface |
| `BimLevels` | add, rename, move or delete building levels |
| `BimWallProperties` | open the BIM Wall panel |
| `BimAssemblies` | edit wall types and the product catalog |
| `BimLibrary` | save / load / reset the shared library |
| `BimFloor` | floor a room by clicking in it, or from picked curves |
| `BimRoof` | build a layered roof off a surface you drew |
| `BimSectionStyles` | apply, refresh or remove the per-material section hatching |
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
    LayeredAssembly.cs      the ordered stack; R, cost, weight, and the justification maths
    AssemblyNaming.cs       side-neutral model -> the words a wall or floor actually uses
    WallDefinition.cs       a wall's parameters: baseline, level, top condition, type
    Level.cs                building levels and the wall top modes
    LayeredElements.cs      floors and roofs: the shared element, slab and roof
    OpeningUnit.cs          a window or door type, and the block that draws it
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
    SlabBuilder.cs          floor and roof layer solids, off a boundary or a surface
    WallJoiner.cs           mitred corners, and tees that tie to structure
    WallJunctions.cs        per-layer stopping planes and through-wall notches
  Documents/              the bridge to the Rhino document
    WallBaker.cs            the only code that writes geometry; groups, layers, user text
    SectionPatterns.cs      construction hatch patterns and the per-layer section style
    OpeningBlocks.cs        places your window and door blocks in the rough openings
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

**It compiles.** .NET SDK 8.0.130, `net7.0-windows` target, clean with zero errors
and zero warnings, producing a real `Stratum.rhp` with its `deps.json` and toolbar
beside it. Across ~6,200 lines written without a compiler the first build produced
exactly one error (`RhinoApp.WriteLine` takes at most three format arguments).

**135 assertions run against the compiled code** — `dotnet run --project tests/StratumTests`.
They cover the justification maths, the length parser across six locales, the rules that
decide how each material reads on a section cut, and the tee arithmetic — that the
arriving core lands exactly on the through wall's core face, that its finish layers stop
short at the face, and that the notch is exactly as wide as the arriving core. See below for the two real defects
they and the analyzers caught.

Here is the rest of what was checked and how:

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

**The core arithmetic is tested against the real code.** `tests/StratumTests`
compiles the actual `Core` and `Modeling` sources and asserts the claim the whole
plug-in rests on: with `CoreCenter` justification the core's centre line sits exactly
on the baseline for every combination of layer thicknesses; thickening the exterior
sheathing by 1/4" moves the exterior face out by exactly 1/4" and leaves the interior
face untouched; thickening the interior gypsum does the mirror image; layer ranges
stay contiguous and sum to the total thickness under all six justifications, flipped
and unflipped.

**A silent data-corruption bug was found and fixed.** Number parsing used the ambient
culture. On a comma-decimal locale `double.TryParse("0.75")` treats the period as a
*thousands* separator and returns **75** — so an architect in Berlin typing the
thickness of 3/4" plywood would have got a 75 inch layer, with no error. All user
input now goes through `Units.TryParseNumber`, which excludes thousands separators
outright and accepts both `0.75` and `0,75`. Regression tests run in en-US, de-DE,
fr-FR, sv-SE, en-GB and invariant.

**What is still unverified:** it has not been run *in Rhino*. Compilation and unit
tests cannot tell you whether `Curve.Offset` returns the pieces expected on a
particular polyline, whether a boolean difference succeeds on a given wall, whether
the panel lays out well at a narrow dock width, or whether Rhino accepts the toolbar
file. Those need the application.

## Status, honestly

Everything above is implemented. What is **not** in this version, and would be
the next work:

- **Three-way and four-way junctions** are left square. Clean two-wall corners mitre
  and tees resolve (below), but a point where three walls meet needs a rule about
  which two mitre and which one dies in — a detailing decision that deserves a
  drawing, not a guess.
- **Frame, sash and glazing geometry** is not modelled, by design — a unit names a
  block and your own geometry is placed in the opening instead.
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
