@echo off
setlocal
cd /d C:\Users\casto\NUBIM\Stratum
set LOG=C:\Users\casto\NUBIM\Stratum\_deploy.log
set DOTNET=C:\Program Files\dotnet\dotnet.exe
set OBJ=src\Stratum\obj\Release\net7.0-windows\Stratum.rhp
set BIN=src\Stratum\bin\Release\net7.0-windows\Stratum.rhp
set RHINO=C:\Program Files\Rhino 8\System\Rhino.exe

rem  Rhino locks Stratum.rhp while it runs, and Rhino is registered to load the
rem  build output directly, so the plug-in can only be replaced with Rhino shut.
rem  Run this from Task Scheduler, not as a child of Rhino: the MCP router puts
rem  Rhino in a job object, so anything Rhino spawns is killed along with it -
rem  which is why three earlier attempts at this silently never ran.
rem  No "timeout" either: it needs a console stdin that a scheduled task has not.

echo [%TIME%] waiting for Rhino to close> "%LOG%"
:wait
tasklist /FI "IMAGENAME eq Rhino.exe" 2>nul | find /I "Rhino.exe" >nul
if not errorlevel 1 (
  ping -n 3 127.0.0.1 >nul
  goto wait
)
echo [%TIME%] Rhino closed>> "%LOG%"
dir "%BIN%" | find "Stratum.rhp" >> "%LOG%"
ping -n 4 127.0.0.1 >nul

echo [%TIME%] building>> "%LOG%"
"%DOTNET%" build src\Stratum\Stratum.csproj -c Release -f net7.0-windows -v minimal --nologo >> "%LOG%" 2>&1
echo [%TIME%] dotnet exit %ERRORLEVEL%>> "%LOG%"

echo [%TIME%] copying obj -^> bin>> "%LOG%"
copy /Y "%OBJ%" "%BIN%" >> "%LOG%" 2>&1
echo [%TIME%] copy exit %ERRORLEVEL%>> "%LOG%"
dir "%BIN%" | find "Stratum.rhp" >> "%LOG%"

echo [%TIME%] starting Rhino>> "%LOG%"
start "" "%RHINO%"
echo [%TIME%] done>> "%LOG%"
