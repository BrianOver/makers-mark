@echo off
REM Text/headless game (no Godot needed). Double-click me. Type 'help' at the prompt.
cd /d "%~dp0"
dotnet run --project sim\GameSim.Cli
pause
