@echo off
REM One-click launch of the visual game. Double-click me.
REM
REM THIS IS NOW A THIN WRAPPER, ON PURPOSE.
REM
REM There were two launchers with different behaviour: this .bat (build, import, run) and
REM tools/play.ps1 (the FRESHNESS GATE -- requires the checkout to be ON trunk, fast-forwards it,
REM and REFUSES to launch when stale or diverged, then stamps provenance into the build).
REM The gate exists because of real incidents where a playtest ran against a stale build and the
REM findings were attributed to the wrong code. But the gate was the one you had to know to type,
REM while the double-clickable file at the repo root quietly skipped it -- so the safe path was the
REM one nobody took, which is a bad default no matter how well the gate is written.
REM
REM Now both roads lead through the gate. Every argument is forwarded, including -AllowStale if you
REM genuinely need to run something off-trunk.
REM
REM MM_PLAYTEST_LOG=1 makes the game write one JSONL row per phase tick to
REM runs/playtest/session-<unix>.jsonl -- gold, materials, shelf, items, heroes alive, refusals and
REM seconds elapsed. The point is that a play session produces DATA, so nobody has to write down
REM their gold on day 5 by hand and nobody has to reconstruct a bug from memory afterwards.
setlocal
set "MM_PLAYTEST_LOG=1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\play.ps1" %*
if errorlevel 1 (
  echo.
  echo   Launch refused or failed -- read the message above.
  echo   If it says the checkout is stale or diverged, that is the freshness gate doing its job.
  echo.
  pause
  exit /b 1
)
