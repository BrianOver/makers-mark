@echo off
REM ============================================================================
REM  THE launcher. There is exactly one. Double-click me.
REM
REM  Optional args (you will almost never want any of these):
REM    stale     launch even if this checkout is behind/diverged from origin/main
REM    verify    run the checks + build + import, then STOP without opening the game
REM
REM  Why one file: there used to be play.bat AND tools/play.ps1 AND play-cli.bat,
REM  they behaved differently, and the double-clickable one skipped the staleness
REM  checks -- so the safe path was the one nobody took. Everything that used to
REM  live in play.ps1 is inlined below. If you are tempted to add a second
REM  launcher, edit this one instead.
REM
REM  There is also exactly ONE checkout: the shared root. It hosts agent worktrees
REM  under .claude\worktrees AND it is the copy you double-click. A separate "play"
REM  worktree used to exist and this file used to refuse to launch from the root
REM  because of it -- that guard outlived the worktree it protected, so it is gone.
REM
REM  What it does, in order:
REM    1. refuses unless this checkout is on main and has no uncommitted code
REM    2. fast-forwards to origin/main, refuses if the two have diverged
REM    3. stamps branch/sha into the in-game corner label
REM    4. builds, reimports assets, and launches with session logging on
REM ============================================================================
setlocal EnableDelayedExpansion
pushd "%~dp0"

set "ALLOW_STALE="
set "VERIFY_ONLY="
:parseargs
if "%~1"=="" goto argsdone
if /i "%~1"=="stale"     set "ALLOW_STALE=1"
if /i "%~1"=="verify"    set "VERIFY_ONLY=1"
shift
goto parseargs
:argsdone

REM ---- Godot -----------------------------------------------------------------
set "GODOT=%GODOT_BIN%"
if "%GODOT%"=="" set "GODOT=C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe"
if not exist "%GODOT%" (
  echo.
  echo   Godot 4.6.3-stable .NET/mono not found:
  echo     %GODOT%
  echo   Install it there, or set GODOT_BIN to your copy.
  echo.
  goto fail
)

REM ---- 1. on main, and clean? ------------------------------------------------
if defined ALLOW_STALE (
  echo ==== stale: freshness checks BYPASSED -- this may NOT be the latest build ====
  goto gatedone
)

for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%B"
if not "!BRANCH!"=="main" (
  echo.
  echo   REFUSING TO LAUNCH -- this checkout is on '!BRANCH!', not 'main'.
  echo   Playtests must run against trunk, or findings get blamed on the wrong code.
  echo     Fix: git checkout main       ^(or run: play.bat stale^)
  echo.
  goto fail
)

REM Only CODE counts as dirty. Reimported .import metadata churns constantly and
REM is not a reason to refuse a launch.
set "DIRTY="
for /f "delims=" %%D in ('git status --porcelain -- "*.cs" "*.tscn" "*.gd" "project.godot"') do set "DIRTY=%%D"
if defined DIRTY (
  echo.
  echo   REFUSING TO LAUNCH -- uncommitted code in the play checkout:
  echo     !DIRTY!
  echo   This checkout tracks main; edit code in a dev worktree instead.
  echo.
  goto fail
)

REM ---- 2. fast-forward to origin/main ---------------------------------------
git fetch --quiet origin main 2>nul
if errorlevel 1 echo   ^(offline -- using the local main^)

set "BEHIND=0"
set "AHEAD=0"
git rev-parse --verify --quiet origin/main >nul 2>&1
if not errorlevel 1 (
  for /f %%N in ('git rev-list --count HEAD..origin/main') do set "BEHIND=%%N"
  for /f %%N in ('git rev-list --count origin/main..HEAD') do set "AHEAD=%%N"
)

if !BEHIND! GTR 0 if !AHEAD! GTR 0 (
  echo.
  echo   REFUSING TO LAUNCH -- main and origin/main have diverged
  echo   ^(ahead !AHEAD!, behind !BEHIND!^). Cannot silently pick a side.
  echo     Fix: git pull --rebase origin main
  echo.
  goto fail
)

if !BEHIND! GTR 0 (
  echo updating to the newest main ^(!BEHIND! new commit^(s^)^)...
  call :quarantine
  git merge --ff-only origin/main >nul || goto fffailed
)

if !AHEAD! GTR 0 echo   note: local main is !AHEAD! commit^(s^) ahead of origin -- unpushed.

:gatedone

REM ---- 3. provenance stamp (the in-game corner label) ------------------------
for /f "delims=" %%S in ('git rev-parse --short HEAD') do set "SHA=%%S"
for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%B"
set "STATE=clean"
for /f "delims=" %%D in ('git status --porcelain') do set "STATE=dirty"
set "STAMP=!BRANCH! @ !SHA! ^| !STATE!"
echo ==== PLAYTEST BUILD ====
echo !BRANCH! @ !SHA! ^| !STATE!
> "godot\assets\build_info.txt" echo !BRANCH! @ !SHA! ^| !STATE!

REM ---- 4. build, import, launch ---------------------------------------------
echo building...
dotnet build "godot\GodotClient.csproj" --nologo -v q
if errorlevel 1 (
  echo.
  echo   BUILD FAILED -- not launching.
  echo.
  goto fail
)

echo importing assets...
"%GODOT%" --path "godot" --headless --import --quit >nul 2>&1

if defined VERIFY_ONLY (
  echo verify: checks + build + import OK, not launching.
  popd
  endlocal
  exit /b 0
)

REM MM_PLAYTEST_LOG=1 makes the session write runs/playtest/session-<unix>.jsonl:
REM one row per phase tick plus one per craft-overlay open/finish/abandon. The point
REM is that playing produces data, so nobody reconstructs a bug from memory after.
set "MM_PLAYTEST_LOG=1"
echo launching...
"%GODOT%" --path "godot"
popd
endlocal
exit /b 0

:fffailed
echo.
echo   COULD NOT FAST-FORWARD to origin/main.
echo   You are !BEHIND! commit^(s^) behind, so the game this would launch is NOT
echo   what is on main. Resolve the merge here before playing -- launching now
echo   would playtest the wrong code and blame the findings on it.
echo.
goto fail

REM ---------------------------------------------------------------------------
REM  An untracked file that ALSO exists on origin/main aborts --ff-only with
REM  "untracked working tree files would be overwritten by merge". Agent sessions
REM  have stranded exactly such files in this checkout before (a loose copy of
REM  docs/design/MAKERS-MARK.md did it), and the way that failure reached a human
REM  was: work merged to main, launcher silently refused to advance, game looked
REM  like the feature was never built. Move the blockers aside and let the
REM  fast-forward through -- they are untracked, so nothing tracked is at risk.
REM ---------------------------------------------------------------------------
:quarantine
set "QDIR=%TEMP%\makers-mark-quarantine"
set "QMOVED="
REM -uall, not --untracked-files=all: cmd's for /f parser chokes on the '=' and
REM the loop silently iterates zero times. Tested -- the long form reads as a
REM working quarantine that never quarantines anything.
for /f "tokens=1,*" %%A in ('git status --porcelain -uall') do (
  if "%%A"=="??" (
    git cat-file -e "origin/main:%%B" 2>nul
    if not errorlevel 1 (
      if not exist "!QDIR!" mkdir "!QDIR!" >nul 2>&1
      echo   setting aside untracked %%B ^(origin/main has its own copy^)
      move /y "%%B" "!QDIR!\" >nul 2>&1
      set "QMOVED=1"
    )
  )
)
if defined QMOVED echo   ^(moved to !QDIR! -- delete them once you have looked^)
goto :eof

:fail
popd
endlocal
pause
exit /b 1
