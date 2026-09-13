<#
  Builds Stratum and installs it where Rhino actually loads it from.

  Why this script exists
  ----------------------
  Rhino registers a plug-in by its GUID, not by its path. Once Stratum has been
  installed once, dragging a newly built .rhp onto the Rhino window does nothing
  visible: Rhino recognises the GUID, decides the plug-in is already installed,
  and goes on loading the file at the registered path.

  That path is also locked for as long as Rhino is running, so a normal
  "dotnet build -c Release" cannot replace it - the compile succeeds, the copy
  into bin\ fails, and the stale .rhp stays exactly where it was. The result is
  a build that looks green while Rhino keeps loading last week's code.

  So: close Rhino, build, copy over the registered file, reopen. That is all this
  script does, with checks at each step so a silent failure is impossible.

  Usage:
    pwsh -File deploy.ps1              # build Release and install
    pwsh -File deploy.ps1 -Dbg         # build Debug instead
    pwsh -File deploy.ps1 -Launch      # install, then start Rhino
#>

[CmdletBinding()]
param(
  # Named -Dbg rather than -Debug: PowerShell reserves -Debug as a common
  # parameter, and redeclaring it is a hard error before the script even runs.
  [switch]$Dbg,
  [switch]$Launch
)

$ErrorActionPreference = 'Stop'

$root          = $PSScriptRoot
$project       = Join-Path $root 'src\Stratum\Stratum.csproj'
$configuration = if ($Dbg) { 'Debug' } else { 'Release' }
$framework     = 'net7.0-windows'
$outputDir     = Join-Path $root "src\Stratum\bin\$configuration\$framework"
$targetRhp     = Join-Path $outputDir 'Stratum.rhp'

# ---- 1. Rhino must not be running -----------------------------------------
# It holds the .rhp open, and the build's copy step would fail against it.

$rhino = Get-Process -Name 'Rhino' -ErrorAction SilentlyContinue
if ($rhino) {
  Write-Host ''
  Write-Host "  Rhino is running (PID $($rhino.Id -join ', '))." -ForegroundColor Yellow
  Write-Host '  It locks the .rhp, so the new build cannot replace it.'
  Write-Host '  Close Rhino and run this again.'
  Write-Host ''
  exit 1
}

# ---- 2. Build ---------------------------------------------------------------

Write-Host ''
Write-Host "  Building $configuration ($framework)..." -ForegroundColor Cyan

& dotnet build $project -f $framework -c $configuration -v quiet --nologo
if ($LASTEXITCODE -ne 0) {
  Write-Host '  Build failed.' -ForegroundColor Red
  exit $LASTEXITCODE
}

if (-not (Test-Path $targetRhp)) {
  Write-Host "  Build reported success but $targetRhp is missing." -ForegroundColor Red
  exit 1
}

# ---- 3. Report where Rhino will load it from --------------------------------
# If the registered path is somewhere else entirely, say so rather than letting
# the user wonder why a green build changed nothing.

$registered = $null
try {
  Get-ChildItem 'HKCU:\Software\McNeel\Rhinoceros' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'Plug-?Ins' } |
    ForEach-Object {
      $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
      if ($props.FileName -match 'Stratum\.rhp$') { $registered = $props.FileName }
    }
} catch { }

$stamp = (Get-Item $targetRhp).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')

Write-Host ''
Write-Host '  Installed.' -ForegroundColor Green
Write-Host "    file  $targetRhp"
Write-Host "    built $stamp"

if ($registered) {
  if ($registered -ieq $targetRhp) {
    Write-Host '    Rhino loads this exact file.' -ForegroundColor Green
  }
  else {
    # The build went somewhere Rhino is not looking. Copy it across, because the
    # registration is what decides which file actually runs.
    Write-Host ''
    Write-Host '    Rhino is registered to a different path:' -ForegroundColor Yellow
    Write-Host "      $registered"
    Write-Host '    Copying the new build over it...'

    $registeredDir = Split-Path $registered -Parent
    if (-not (Test-Path $registeredDir)) { New-Item -ItemType Directory -Path $registeredDir -Force | Out-Null }

    Copy-Item $targetRhp $registered -Force

    # The toolbar file has to travel with it: Rhino finds the buttons by looking
    # for a .rui beside the .rhp that shares its name.
    $rui = Join-Path $outputDir 'Stratum.rui'
    if (Test-Path $rui) { Copy-Item $rui (Join-Path $registeredDir 'Stratum.rui') -Force }

    $deps = Join-Path $outputDir 'Stratum.deps.json'
    if (Test-Path $deps) { Copy-Item $deps (Join-Path $registeredDir 'Stratum.deps.json') -Force }

    Write-Host '    Done.' -ForegroundColor Green
  }
}
else {
  Write-Host ''
  Write-Host '    Stratum is not registered yet - this is a first install.' -ForegroundColor Yellow
  Write-Host '    Start Rhino and drag this .rhp onto the window once;'
  Write-Host '    after that, this script updates it in place.'
}

Write-Host ''

if ($Launch) {
  $exe = 'C:\Program Files\Rhino 8\System\Rhino.exe'
  if (Test-Path $exe) {
    Write-Host '  Starting Rhino...' -ForegroundColor Cyan
    Start-Process $exe
  }
  else {
    Write-Host "  Rhino not found at $exe" -ForegroundColor Yellow
  }
}
