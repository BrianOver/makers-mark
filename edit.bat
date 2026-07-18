@echo off
REM Open the game in the Godot EDITOR (for building scenes/tweaking). Double-click me.
setlocal
set "GODOT=%GODOT_BIN%"
if "%GODOT%"=="" set "GODOT=C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
if not exist "%GODOT%" (
  echo.
  echo   Godot 4.6.3-stable .NET/mono not found. Set GODOT_BIN or install to C:\Tools\Godot\...
  echo.
  pause
  exit /b 1
)
"%GODOT%" --editor --path "%~dp0godot"
