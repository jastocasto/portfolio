# Stratum — open items

A live list, not a record. The dated findings files (`RHINO-RUN-*.md`) are the
record; anything still outstanding gets restated here so it is in one place.

Last touched: 2026-09-13

---

## Blocked on a decision

### O-1 · Annotation and hatch scales are metric-only  *(deferred by agreement, 2026-09-13)*

`tds_metric_template.3dm` carries 36 dimension styles and 29 hatch patterns,
all authored in millimetres. `tds_imperial_template.3dm` is a different and
older layer system — 65 layers, AIA-nested (`Plan::A-Wall::A-Wall`), against
the metric template's 129 flat discipline-prefixed layers (`Env-Wall-Brick`,
`Struct-Wall-WdStud`, `G-Anno-Dims`, `G-Obj-Datums`). **The two share no
layers at all.** The metric one is the current system.

So an inches document that uses the real layer system has to take those layers
from the metric template, and then its annotation is wrong: text height 2.5
reads as 2.5 inches rather than 2.5 mm, and hatch scales are out by 25.4.

Open: convert the dimension styles and hatch patterns to imperial equivalents,
or keep imperial files model-only and handle annotation separately. Deferred
deliberately — do not let a later session silently pick one.

Do not edit the two templates. Both originals are untouched; the cleaned
copies are `tds_metric_template_clean.3dm` and
`tds_imperial_template_clean.3dm` beside them.

### O-2 · Stratum's layer tree sits beside the template's, not inside it

Wall solids bake onto `Stratum::Walls::W1::01 Fiber cement lap siding, …`.
The template's wall layers are `Struct-Wall-WdStud`, `Env-Wall-Brick`,
`Struct-Shth-OSB` and so on. Both exist in the same document and neither knows
about the other.

This decides how line weight, colour and print control work in sheets, because
that is all driven off layers. It has to be settled before there is a project's
worth of geometry on the wrong branch — moving it later is worse than choosing
now.

### O-3 · Which drawing engine survives  *(PRODUCTION-SYSTEM.md Q10)*

Gates R-1 below. Nothing to build until it is answered.

---

## Specified, waiting on something else

### R-1 · Suppress the drawn seam between matching materials

Confirmed as a requirement. Full spec in `RHINO-RUN-2026-09-13.md` §R-1.
Blocked on O-3: there is no 2D output stage in Stratum to put the rule in.
`SectionPatterns.cs` assigns hatch patterns to layers; that is all "section"
currently means. No `Make2D`, no silhouette extraction.

When it is unblocked it needs a material *family* on `MaterialProduct`,
coarser than `ProductId` and finer than `LayerFunction`, so that 5/8" Type X
butting 1/2" regular still reads as gypsum meeting gypsum.

---

## Queued work

### D-A · Corners are always mitred  *(next)*

`WallJoiner.MakeMiter` sets `FacePlane = CorePlane`. A 45° line cuts every
layer and it prints. Replace with a per-layer butt driven by `Priority`.
See `RHINO-RUN-2026-09-13.md` §D-A.

### Then, untouched and untested

Openings at a junction. Section styles. Raked tops against a roof. Roofs.
Schedules. Three- and four-way junctions — `WallJoiner` leaves anything past
two walls at a point square, by design, and that has never been exercised.

---

## Closed

- **D-B** — a notch discarded everything past it. Fixed 2026-09-13; a layer is
  now `List<Brep>` through the whole pipeline. Verified on the imperial rig:
  interior gypsum returns as two solids, 0.000→118.250 and 121.750→237.238,
  gap exactly 3.500" = the partition's 2x4 core.
- **Base elevation from the CPlane** — `BimWall` read the construction plane's
  Z straight into the wall, which built walls at arbitrary heights that looked
  right in plan and never met each other. Fixed 2026-09-13; the CPlane now only
  selects the level.
- **Rhino startup** — template detached, five plug-ins set to load silently.
  See `RHINO-RUN-2026-09-13.md` §8.

---

## Notes for whoever automates this next

Deploying the plug-in cannot be done from a process Rhino spawns. The MCP
router runs Rhino inside a **job object**, so any watcher started from inside
Rhino is killed the moment Rhino exits — which is exactly when it needs to run.
Three attempts failed silently this way. `wait-and-deploy.cmd` works when
launched from Task Scheduler, which is outside the job:

    schtasks /create /tn StratumDeploy /tr "cmd /c C:\Users\casto\NUBIM\Stratum\wait-and-deploy.cmd" /sc once /st HH:MM /it /f

Rhino is registered against the build output itself
(`HKCU\Software\McNeel\Rhinoceros\8.0\Plug-Ins\bc93f43b-…\PlugIn` →
`src\Stratum\bin\Release\net7.0-windows\Stratum.rhp`), so a successful build is
the install. `dotnet` is not on PATH in that context — call it absolutely.
