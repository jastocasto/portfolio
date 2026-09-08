# Start here — getting Stratum running and checking it works

A step-by-step walkthrough for the first time you sit down at your Windows machine.
Follow it top to bottom. Each step says **what to do** and **what you should see**, so
you can tell immediately whether it worked.

If something doesn't match, stop at that step and tell me what you saw — that's more
useful than pushing on.

---

## Part 1 — Install the tools (once, about 10 minutes)

### Step 1. Check you have Rhino 8

Open Rhino, then **Help → About Rhinoceros**. Look at the version number.

- **You should see:** Rhino 8, build 8.19 or higher.
- If it's older than 8.19, update Rhino first. The plug-in is built against 8.19.

### Step 2. Install the .NET SDK

Download and install **.NET SDK 8.0** from
<https://dotnet.microsoft.com/download/dotnet/8.0> — pick **x64** under "SDK".

Then open **Command Prompt** (press Start, type `cmd`, Enter) and run:

```
dotnet --version
```

- **You should see:** a version number like `8.0.404`.
- If you see "not recognized", close the Command Prompt, open a new one, and try again.
  Windows needs a fresh window after installing.

### Step 3. Get the code

If you don't already have the repository on your machine, in Command Prompt:

```
cd %USERPROFILE%\Documents
git clone https://github.com/jastocasto/portfolio.git
cd portfolio
git checkout claude/parametric-bm-wall-plugin-0wxzmp
```

- **You should see:** a folder `Documents\portfolio` containing a folder called `Stratum`.

If you already have it, just `cd` into it and run the `git checkout` line, then
`git pull`.

---

## Part 2 — Build the plug-in (2 minutes)

### Step 4. Build it

In Command Prompt, from inside the `portfolio` folder:

```
cd Stratum
dotnet build src\Stratum\Stratum.csproj -c Release
```

- **You should see:** `Build succeeded.` and `0 Error(s)`.
- The first run takes a minute while it downloads RhinoCommon. After that it's seconds.

### Step 5. Find the plug-in file

The file you need is:

```
Stratum\src\Stratum\bin\Release\net7.0-windows\Stratum.rhp
```

- **You should see:** `Stratum.rhp` (about 170 KB) and `Stratum.rui` sitting next to it.
- **Both files matter.** `Stratum.rhp` is the plug-in. `Stratum.rui` is the toolbar, and
  Rhino only finds it if it stays in the same folder as the `.rhp`.

### Step 6. Run the self-check (optional but worth it)

```
dotnet run --project tests\StratumTests -c Release
```

- **You should see:** a long list of `PASS` lines ending in `ALL 211 CHECKS PASSED`.
- This checks the maths — layer positions, junctions, levels, unit sizes — without
  needing Rhino. If this fails, don't bother loading the plug-in; tell me what failed.

---

## Part 3 — Load it into Rhino (1 minute)

### Step 7. Install the plug-in

Open Rhino 8 with a **new blank model in inches or feet**. Then drag
`Stratum.rhp` from File Explorer and drop it onto the Rhino window.

- **You should see:** a message in the command line:
  `Stratum BIM 0.9.0 loaded. Type BimWall to draw, BimHelp for the command list.`
- If Rhino asks whether to trust the plug-in, say yes.

### Step 8. Confirm the commands are there

Type in the Rhino command line:

```
BimHelp
```

- **You should see:** a list of 14 commands printed — `BimWall`, `BimOpening`,
  `BimFloor`, `BimRoof` and so on.
- If `BimHelp` isn't recognised, the plug-in didn't load. Check
  **Tools → Options → Plug-ins** and look for Stratum.

---

## Part 4 — Check it actually works

Do these in order in one blank model. Roughly 15 minutes.

### Step 9. Draw a wall

Type `BimWall`. Click two points about 20 feet apart. Press **Enter**.

- **You should see:** while you drag, the whole layered wall drawn in 3-D under the
  cursor — not a single box, but separate coloured layers.
- After Enter: a wall made of several solids. Switch to a **Shaded** view if you're in
  wireframe.

### Step 10. Check the selection behaviour

- **Click** the wall once → the *whole* wall selects (all layers).
- **Ctrl+Shift+click** one layer → just that one layer selects.

This is Rhino's normal group behaviour; the layers are one group.

### Step 11. Open the properties panel

Type `BimWallProperties`. Select the wall.

- **You should see:** a panel with a **drawing of the wall's cross-section** at the top —
  exterior on the left, each layer in its own colour, the structural core outlined heavily,
  and a blue line showing where the reference line sits.
- Below it: a table of layers with product, thickness, R-value and cost per square foot.

### Step 12. The important one — swap a product

In the panel's layer table, find the **sheathing** row. Click its **Product** cell and
change it to a thicker plywood (e.g. `Plywood CDX, 3/4"`).

- **You should see:** the wall gets thicker, **but only on the outside**. The studs do
  not move. The interior face stays exactly where it was.
- The thickness, R-value and cost totals underneath update.

This is the core idea of the whole plug-in. If this doesn't behave that way, that's the
most important thing to tell me.

### Step 13. Draw a corner and a T-junction

Type `BimWall` and draw two walls meeting at a right angle. Then draw a third wall that
runs into the **middle** of one of them and stops.

- **At the corner:** the layers should mitre — cut on the diagonal, not overlapping.
- **At the T:** the arriving wall's studs should run *through* the other wall's drywall to
  land on its studs, and that drywall should be notched exactly as wide as those studs.

This is the newest wall geometry. Zoom in on the T and look carefully.

### Step 14. Put a window in

Type `BimOpening`. Select the wall. Choose a **Unit** from the list (e.g.
`3050doublehung`). Click a point along the wall.

- **You should see:** a hole cut through every layer, with the layers terminating
  differently from each other — the gypsum wrapping into the reveal, the sheathing
  stopping flush.

### Step 15. Cut a section — the payoff

Type `ClippingPlane` (Rhino's own command) and drag a plane straight through the wall.

- **You should see:** the cut face is **hatched per material** — the studs as wood grain,
  insulation cross-hatched, gypsum light — each with a colour fill behind it, and the
  structural core drawn in a heavier line.
- **Not** white empty boxes. If you get white boxes, run `BimSectionStyles` → `Apply`.

This is what makes the drawings check themselves. Take a section anywhere and the
materials read correctly with no setup.

### Step 16. Levels and a raked wall

1. Type `BimLevels` → `Add`. Name it `Level 2`, elevation `10'-1"`. Press Enter to exit.
2. Draw a sloped surface above a wall (Rhino's `Plane` command, then rotate it).
3. Type `BimWallTop`, select the wall, choose `ToSurface`, pick the sloped surface.

- **You should see:** the wall rises to meet the slope at the roof's own angle — a gable
  end — with **every layer** following the slope, not just the outline.

**This is the least-tested geometry in the plug-in.** Look at it closely.

### Step 17. Floor and roof

1. Draw four walls in a closed loop.
2. Type `BimFloor`, then click a point **inside** the room.
3. Type `BimRoof`, select the sloped surface from Step 16.

- **Floor:** a layered slab lands on the room. The subfloor and finish sit *above* the
  level, the joists and ceiling hang *below* it.
- **Roof:** layers offset off your surface — shingles, sheathing, rafters, ceiling.

If `BimFloor` says it can't find a closed room, your walls don't quite form a loop — use
the `PickCurves` option instead and select a closed rectangle.

### Step 18. Export the schedules

Type `BimSchedule`. Save the CSV somewhere and open it in Excel.

- **You should see:** four tables — a wall schedule, a floor and roof schedule, a window
  schedule and a door schedule — plus a material takeoff listing every layer of every wall
  with quantities and costs.

---

## Part 5 — Making it yours

### Step 19. Put your own prices in

Type `BimAssemblies` → **Products** tab. The **$/sf** column is installed cost per square
foot. Replace the placeholders with your real supplier pricing.

Then **Save to library**. Every future project starts from your numbers.

### Step 20. Plug in your own window and door geometry

This is the hook for the blocks you already have.

1. In Rhino, make a block of one of your window units. **Draw it in the world XY plane as
   if you're standing outside looking at it:** X is the width centred on the origin, Y is
   the height starting at the sill, Z runs back through the wall.
2. Note the block's name.
3. Type `BimAssemblies`, find the matching window type, and put that block name in its
   **block name** field.
4. Type `BimRebuild`.

- **You should see:** your window geometry appear in every opening of that type, oriented
  correctly and grouped with the wall so it moves with it.

If your existing blocks use a different origin or orientation, don't redraw them — tell me
what convention they use and I'll match it.

---

## Which file is which

| File | What it's for |
|---|---|
| `Stratum\src\Stratum\bin\Release\net7.0-windows\Stratum.rhp` | **The plug-in.** Drag this onto Rhino. |
| `Stratum\src\Stratum\bin\Release\net7.0-windows\Stratum.rui` | The toolbar. Must stay beside the `.rhp`. |
| `Stratum\README.md` | What it does and how it's built, in detail. |
| `Stratum\BUILD.md` | Build notes and the short test checklist. |
| `Stratum\START-HERE.md` | This file. |
| `Stratum\src\Stratum\Core\CatalogDefaults.cs` | The starting products and wall types, in code. Easier to edit in `BimAssemblies`. |

---

## If something goes wrong

**The build fails.** Copy the whole error and send it to me. Don't try to fix it — the
errors are usually one line and I'd rather see the real text.

**The plug-in won't load.** Check **Tools → Options → Plug-ins**, search for Stratum.
If it's listed but disabled, tick it. If it's not listed, the drag-and-drop didn't take —
use **Install** in that dialog and browse to the `.rhp`.

**Geometry looks wrong.** Run `BimRebuild` first — it regenerates everything from the
parameters and fixes most oddities. If it's still wrong, take a screenshot; that tells me
far more than a description.

**A wall disappeared after an edit.** Look at the command line for a `Stratum:` warning —
it names the layer and the reason. Booleans occasionally fail on awkward geometry, and it
tells you rather than silently dropping things.

---

## Honest status

Everything above is built and compiles clean, and the maths is covered by automated tests.
But **the plug-in has never been run inside Rhino** — I have no Rhino here. Compilation and
unit tests can't tell you whether a boolean succeeds on a particular wall or whether a panel
lays out well at a narrow dock width.

The parts most likely to need adjusting, in order:

1. **Roof surface offsetting** (Step 17) — the least predictable operation in the plug-in.
2. **The raked wall top** (Step 16) — newest geometry.
3. **T-junctions** (Step 13) — the logic is tested, the boolean result isn't.
4. **Panel layout** — it's built to reflow, but has never been seen on screen.

Steps 9–12 are the most established and should just work.
