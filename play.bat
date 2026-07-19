@echo off
REM One-click launch of the visual game (no editor). Double-click me.
setlocal
set "GODOT=%GODOT_BIN%"
if "%GODOT%"=="" set "GODOT=C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
if not exist "%GODOT%" (
  echo.
  echo   Godot 4.6.3-stable .NET/mono not found.
  echo   Install it, or set GODOT_BIN to its .exe path.
  echo   Expected: C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe
  echo.
  pause
  exit /b 1
)
REM Build the C# game assembly first — Godot mono loads it at boot, and a fresh checkout
REM has none (launching without it crashes before the first frame).
dotnet build "%~dp0godot\GodotClient.csproj" --nologo
if errorlevel 1 (
  echo.
  echo   C# build failed — fix the errors above, then rerun.
  echo.
  pause
  exit /b 1
)

REM R8: headless import pre-pass so committed art (.png + .png.import) renders on a fresh
REM checkout — GD.Load needs the .godot/imported/*.ctex cache, which a plain clone lacks.
"%GODOT%" --path "%~dp0godot" --headless --import --quit

"%GODOT%" --path "%~dp0godot"
