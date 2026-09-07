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

## First-compile expectations

This code was written without a compiler and without Rhino, so the first build is
a real build. But it is not a shot in the dark either — see the verification
section of `README.md`. Every one of the 192 API references was checked against
the metadata of the actual `RhinoCommon.dll`, `Rhino.UI.dll` and `Eto.dll`, and
the justification arithmetic is unit-tested in `tests/wall_math_test.py`.

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

What static verification **cannot** tell you, and what to actually watch for:

1. **Type inference.** Every generic call was checked for existence and shape,
   but the C# compiler may still want an explicit type argument somewhere the
   metadata cannot predict.
2. **Geometry behaviour.** Whether `Curve.Offset` returns the pieces expected on
   a particular polyline, and whether a boolean difference succeeds on a
   particular wall, is a runtime question. Both are already defensive — offset
   falls back to a translation, and a failed boolean leaves the layer uncut with
   a warning on the command line — but the fallbacks have not been exercised.
3. **Panel layout at a narrow dock width.** The panel is built with
   `DynamicLayout` and should reflow, but it has never been seen.

## Where things live at run time

| what | where |
|---|---|
| walls, wall types, product catalog | inside the `.3dm`, written by the plug-in's `WriteDocument` |
| shared office library | `%APPDATA%\Stratum\StratumLibrary.3dmlib` |
| per-solid BIM data | 3dm attribute user text on each layer solid (`Stratum:*`) |
| Rhino layers | `Stratum::Walls::<wall type code>::<nn product>` |
