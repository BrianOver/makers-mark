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
REM R8: headless import pre-pass so committed art (.png + .png.import) renders on a fresh
REM checkout — GD.Load needs the .godot/imported/*.ctex cache, which a plain clone lacks.
"%GODOT%" --path "%~dp0godot" --headless --import --quit

"%GODOT%" --path "%~dp0godot"
