# Building and installing Stratum

## What you need

- **Windows** with **Rhino 8** installed (Rhino 8.0 or later).
- **.NET SDK 7.0** — Rhino 8 runs plug-ins on .NET 7.
  <https://dotnet.microsoft.com/download/dotnet/7.0>
- **Rhino 8.19 or later** if you build the `net7.0-windows` target as pinned
  (see the version note below).
- **.NET Framework 4.8 developer pack** — only if you want the `net48` target for
  Rhino 8's legacy runtime. Drop `net48` from `<TargetFrameworks>` in
  `src/Stratum/Stratum.csproj` if you do not.
- Visual Studio 2022 (17.8+) or just the `dotnet` CLI.

## Build

```
cd Stratum
dotnet build src/Stratum/Stratum.csproj -c Release
```

This project **has been compiled** — on Linux with .NET SDK 8.0.130, building the
`net7.0-windows` target, clean with zero warnings. The exact command used was:

```
dotnet build src/Stratum/Stratum.csproj -f net7.0-windows -c Release
```

## Tests

```
dotnet run --project tests/StratumTests -c Release
```

96 assertions against the real compiled code: the justification maths (the core stays
on the baseline, faces move only on their own side, layer ranges stay contiguous under
all six justifications flipped and unflipped) and the length parser across six locales.
The test project compiles the `Core` and `Modeling` sources directly rather than
referencing the plug-in, because the .NET host will not load an assembly named `.rhp`.

Output:

```
src/Stratum/bin/Release/net7.0-windows/Stratum.rhp
src/Stratum/bin/Release/net7.0-windows/Stratum.rui
src/Stratum/bin/Release/net7.0-windows/Stratum.deps.json
```

`Stratum.rui` must stay beside `Stratum.rhp` — that is how Rhino finds the
toolbar.

### The two csproj settings that actually matter

```xml
<TargetExt>.rhp</TargetExt>              <!-- a Rhino plug-in is a .NET dll with a .rhp extension -->
<EnableDynamicLoading>true</EnableDynamicLoading>   <!-- emits the deps.json Rhino needs on .NET 7 -->
```

and, on every Rhino package reference:

```xml
ExcludeAssets="runtime"
```

Without that last one the build copies RhinoCommon.dll next to your plug-in,
Rhino loads two copies of it, and the plug-in fails at load with type-identity
errors that are miserable to diagnose. It is the single most common way a Rhino
plug-in build goes wrong.

### Do not add an Eto package reference

The RhinoCommon package ships `Eto.dll`, `Ed.Eto.dll` and `Rhino.UI.dll`
alongside `RhinoCommon.dll`, so the Eto you compile against is by construction
the Eto that Rhino loads. Adding a separate `Eto.Forms` PackageReference pulls a
different build — RhinoCommon 8.19 carries Eto 2.9, 8.34 carries 2.11, and the
public NuGet `Eto.Forms` is neither — and the two identities collide at load
time. There is exactly one package reference in this project, and that is
correct.

### About the pinned version

`8.19.25132.1001` is the **earliest RhinoCommon that publishes a `lib/net7.0`
asset**. Every package from 8.0 through 8.18 is net48-only; a `net7.0-windows`
project can consume those only through NuGet's asset fallback, which is what the
`NoWarn>NU1701` in most Rhino plug-in templates is quietly papering over.

Raising the version is safe. Lowering it below 8.19 means adding `NU1701` back to
`NoWarn` and accepting the fallback. If you hit an API mismatch, match the package
to your installed Rhino: `Rhino → Help → About` gives the build, and the version
list is at <https://www.nuget.org/packages/RhinoCommon>.

## Install for testing

Drag `Stratum.rhp` onto an open Rhino 8 window, or `Tools → Options → Plug-ins →
Install`. Then:

```
BimHelp        list the commands
BimWall        draw a wall
```

To debug, set the project's launch target to
`C:\Program Files\Rhino 8\System\Rhino.exe` and attach; Visual Studio's Rhino
plug-in template does this for you if you would rather start from it.

## Package for distribution

```
"C:\Program Files\Rhino 8\System\Yak.exe" build --platform win
"C:\Program Files\Rhino 8\System\Yak.exe" push stratum-0.9.0-rh8_0-any.yak
```

Run it from the folder holding the built `.rhp`, with `manifest.yml` beside it.

## What the first compile actually found

The build is green, so this section is history rather than warning. Across ~6,200
lines written without a compiler, the first build produced **one** error:
`RhinoApp.WriteLine` accepts at most three format arguments, and one call in
`BimOpeningCommand` passed five. Everything else compiled.

Two further real defects were found by turning the analyzers up afterwards:

- **`UseWindowsForms` was unnecessary** and has been removed. The entire UI is
  Eto.Forms; there is no `System.Windows.Forms` type in the project. It was pulling
  in the WindowsDesktop SDK for nothing and stopping the project building anywhere
  but Windows.
- **Culture-dependent number parsing.** `double.TryParse` without an explicit
  culture reads `"0.75"` on a comma-decimal locale as *seventy-five* — the period is
  treated as a thousands separator. An architect in Berlin typing the thickness of
  3/4" plywood would have silently got a 75 inch layer. All user input now goes
  through `Units.TryParseNumber`, which uses `NumberStyles.Float` (no thousands
  separators, so the misreading is impossible) and accepts both `0.75` and `0,75`
  everywhere. Regression tests cover en-US, de-DE, fr-FR, sv-SE, en-GB and invariant.

The API-surface verification described in `README.md` still stands: every one of the
192 references was checked against the metadata of the real `RhinoCommon.dll`,
`Rhino.UI.dll` and `Eto.dll` before the first build, which is why the build was one
error rather than fifty.

Verified specifically, because these were the risky ones:

- `Binding.Delegate(getValue, setValue = default, …)` — the four trailing
  parameters genuinely are optional, so the one- and two-argument calls compile.
- `TextBoxCell` / `CheckBoxCell` / `ComboBoxCell` all inherit `Binding` from
  `SingleValueCell<T>` with `T` = `string`, `bool?` and `object` respectively —
  which is what the panel's bindings are typed as.
- `DynamicLayout.Add(Control control, bool? xscale, bool? yscale)` — the
  parameter *names* match the named arguments used in the layout code.
- `Panels.RegisterPanel(PlugIn, Type, string, System.Drawing.Icon)` exists, and
  the `(System.Drawing.Icon)null` cast is needed to disambiguate it from the
  `Assembly`-based overload.
- `Rhino.FileIO.BinaryArchiveFile`, `Brep.Trim(Plane, double)`,
  `Extrusion.Create(Curve, double, bool)`, `Curve.Offset(Point3d, Vector3d, double, double, CurveOffsetCornerStyle)`,
  `ObjectTable.Select(Guid, bool, bool)` and `ArchivableDictionary.Set(string, ArchivableDictionary)`
  are all present with the signatures the code uses.

What is still **not** verified, because it needs Rhino itself:

1. **Geometry behaviour.** Whether `Curve.Offset` returns the pieces expected on a
   particular polyline, and whether a boolean difference succeeds on a particular
   wall, is a runtime question. Both are defensive — offset falls back to a
   translation, a failed boolean leaves the layer uncut and says so on the command
   line — but the fallbacks have not been exercised.
2. **Panel layout at a narrow dock width.** Built with `DynamicLayout`, so it should
   reflow, but it has never been seen on screen.
3. **The toolbar file.** `Stratum.rui` is well-formed XML matching Rhino's schema,
   but Rhino has not been asked to load it.

## Where things live at run time

| what | where |
|---|---|
| walls, wall types, product catalog | inside the `.3dm`, written by the plug-in's `WriteDocument` |
| shared office library | `%APPDATA%\Stratum\StratumLibrary.3dmlib` |
| per-solid BIM data | 3dm attribute user text on each layer solid (`Stratum:*`) |
| Rhino layers | `Stratum::Walls::<wall type code>::<nn product>` |
