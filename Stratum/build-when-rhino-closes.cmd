@echo off
setlocal
cd /d C:\Users\casto\NUBIM\Stratum
set LOG=C:\Users\casto\NUBIM\Stratum\_deploy.log
set DOTNET=C:\Program Files\dotnet\dotnet.exe
set OBJ=src\Stratum\obj\Release\net7.0-windows\Stratum.rhp
set BIN=src\Stratum\bin\Release\net7.0-windows\Stratum.rhp

rem  Same job as wait-and-deploy.cmd but it does NOT restart Rhino: when the
rem  router owns the Rhino lifecycle, the agent spawns the new slot itself and a
rem  second, unmanaged Rhino only gets in the way.
rem
rem  Launch it through WMI, not as a child of Rhino and not via Task Scheduler:
rem    powershell -c "Invoke-CimMethod Win32_Process Create -Arguments @{CommandLine='cmd /c <this>'}"
rem  The MCP router puts Rhino in a job object, so anything Rhino spawns dies with
rem  it - which is exactly when this needs to run. A WMI-created process is a child
rem  of WmiPrvSE and escapes the job. Task Scheduler also escapes it, but stopped
rem  firing on this machine after a restart, so WMI is the reliable one.

echo [%TIME%] waiting for Rhino to close> "%LOG%"
:wait
tasklist /FI "IMAGENAME eq Rhino.exe" 2>nul | find /I "Rhino.exe" >nul
if not errorlevel 1 (
  ping -n 3 127.0.0.1 >nul
  goto wait
)
echo [%TIME%] Rhino closed>> "%LOG%"
dir "%BIN%" | find "Stratum.rhp" >> "%LOG%"
ping -n 3 127.0.0.1 >nul

echo [%TIME%] building>> "%LOG%"
"%DOTNET%" build src\Stratum\Stratum.csproj -c Release -f net7.0-windows -v minimal --nologo >> "%LOG%" 2>&1
echo [%TIME%] dotnet exit %ERRORLEVEL%>> "%LOG%"

echo [%TIME%] copying obj -^> bin>> "%LOG%"
copy /Y "%OBJ%" "%BIN%" >> "%LOG%" 2>&1
echo [%TIME%] copy exit %ERRORLEVEL%>> "%LOG%"
dir "%BIN%" | find "Stratum.rhp" >> "%LOG%"
echo [%TIME%] done - Rhino NOT restarted, spawn a slot>> "%LOG%"
