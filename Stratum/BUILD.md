# Building and installing Stratum

## What you need

- **Windows** with **Rhino 8** installed (Rhino 8.0 or later).
- **.NET SDK 7.0** — Rhino 8 runs plug-ins on .NET 7.
  <https://dotnet.microsoft.com/download/dotnet/7.0>
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

### If the Eto reference is duplicated

The csproj references `Eto.Forms` explicitly. If your RhinoCommon package already
surfaces Eto and you get a duplicate-assembly warning, delete this line:

```xml
<PackageReference Include="Eto.Forms" Version="2.8.3" ExcludeAssets="runtime" />
```

Match the RhinoCommon version to your installed Rhino if you hit API mismatches:
`Rhino → Help → About` gives the build; the matching NuGet version is on
<https://www.nuget.org/packages/RhinoCommon>.

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

This code was written without a compiler or a Rhino installation available, so
treat the first build as a real build, not a formality. The parts most likely to
need a small correction are:

1. **Eto grid cell bindings** (`src/Stratum/Ui/`) — `Binding.Delegate`,
   `ComboBoxCell.DataStore` and `GridColumn.Editable` have all been stable for
   years, but Eto's binding generics are fussy about inference and may want an
   explicit type argument.
2. **`Rhino.UI.Panels.RegisterPanel` overload** in `StratumPlugIn.cs` — the icon
   argument is cast to `System.Drawing.Icon` to pick an overload; if your
   RhinoCommon exposes a different set, pass a real icon or use the overload your
   version offers.
3. **`Rhino.FileIO.BinaryArchiveFile`** in `AssemblyCatalog.cs` — used for the
   shared library file. Everything that touches it is already wrapped in
   try/catch, so if it needs adjusting only the library feature is affected, not
   the plug-in.

Nothing in `Core/`, `Modeling/` or `Documents/` depends on anything unusual —
those are ordinary RhinoCommon geometry and document APIs.

## Where things live at run time

| what | where |
|---|---|
| walls, wall types, product catalog | inside the `.3dm`, written by the plug-in's `WriteDocument` |
| shared office library | `%APPDATA%\Stratum\StratumLibrary.3dmlib` |
| per-solid BIM data | 3dm attribute user text on each layer solid (`Stratum:*`) |
| Rhino layers | `Stratum::Walls::<wall type code>::<nn product>` |
