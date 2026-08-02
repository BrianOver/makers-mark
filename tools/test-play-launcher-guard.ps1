<#
.SYNOPSIS
  Mutation-check for play.bat's "shared dev checkout" guard (fix/playtest-shell-bugs, item 1:
  two play.bat files on the owner's machine kept confusing him about which one to run).

.DESCRIPTION
  On the owner's machine the repo is checked out twice: a shared checkout that hosts agent
  worktrees under .claude\worktrees, and a separate "play" worktree at <repo>\play meant to be
  the one actually double-clicked. Both copies carry the SAME tracked play.bat, so content can
  never tell them apart -- git rm-ing it from one would remove it from both. The fix is a guard
  inside play.bat itself: refuse to launch (with a redirect message) whenever it finds a nested
  play\play.bat sitting next to it, since only the shared/container checkout has that shape.

  This script proves the guard fires exactly when it should, using three throwaway sandboxes
  under $env:TEMP (never git repos, so play.bat's own branch/clean checks fail fast right after
  the guard -- no real `dotnet build` or Godot launch can be reached from here):

    A. container shape (nested play\play.bat present), no bypass -> MUST refuse with the guard
       message.
    B. container shape + the `container` bypass arg -> guard message MUST be absent.
    C. no nested play\ folder (an ordinary single checkout) -> guard message MUST be absent.

  Every invocation pipes an empty string in as stdin so a refusal path's trailing `pause` can
  never block this script.

  Deliberately never deletes its sandboxes (only creates/overwrites the same three fixed
  directory names) -- avoids needing any destructive filesystem call from this script.

.EXAMPLE
  powershell -File tools/test-play-launcher-guard.ps1
#>
$repoRoot = Split-Path -Parent $PSScriptRoot
$playBat = Join-Path $repoRoot 'play.bat'
if (-not (Test-Path $playBat)) {
    throw "Expected play.bat at $playBat -- run this from a checkout of the repo."
}

function Get-Sandbox {
    param([string]$Name, [switch]$WithNestedPlay)
    $dir = Join-Path $env:TEMP "playbat-guard-test-$Name"
    New-Item -ItemType Directory -Path $dir -Force -ErrorAction Stop | Out-Null
    Copy-Item $playBat (Join-Path $dir 'play.bat') -Force -ErrorAction Stop
    if ($WithNestedPlay) {
        $nested = Join-Path $dir 'play'
        New-Item -ItemType Directory -Path $nested -Force -ErrorAction Stop | Out-Null
        Set-Content -Path (Join-Path $nested 'play.bat') -Value '@echo off' -Encoding ascii -ErrorAction Stop
    }
    return $dir
}

# NOTE: deliberately does not redirect the batch's stderr (no 2>&1 / 2>$null). Windows
# PowerShell 5.1 wraps a native command's stderr lines in NativeCommandError even on success,
# which reads as a terminating failure here for no reason -- git's own expected "not a git
# repository" chatter (these sandboxes are never repos, by design, see the file header) would
# otherwise abort this script instead of just being ignored, which is all it needs.
function Invoke-PlayBat {
    param([string]$Dir, [string[]]$ExtraArgs = @())
    if ($ExtraArgs.Count -gt 0) {
        $out = "" | & "$Dir\play.bat" @ExtraArgs
    } else {
        $out = "" | & "$Dir\play.bat"
    }
    return ($out -join "`n")
}

$guardMessage = 'REFUSING TO LAUNCH -- this is the shared dev checkout'
$failures = @()

# A. container shape, no bypass -> guard MUST fire.
$dirA = Get-Sandbox -Name 'A' -WithNestedPlay
$outA = Invoke-PlayBat -Dir $dirA
if ($outA -notmatch [regex]::Escape($guardMessage)) {
    $failures += "A (container shape, no bypass): guard did NOT fire. Output:`n$outA"
}

# B. container shape + bypass -> guard MUST be silent.
$dirB = Get-Sandbox -Name 'B' -WithNestedPlay
$outB = Invoke-PlayBat -Dir $dirB -ExtraArgs @('container')
if ($outB -match [regex]::Escape($guardMessage)) {
    $failures += "B (container shape + 'container' bypass): guard fired anyway. Output:`n$outB"
}

# C. ordinary single checkout (no nested play\) -> guard MUST be silent.
$dirC = Get-Sandbox -Name 'C'
$outC = Invoke-PlayBat -Dir $dirC
if ($outC -match [regex]::Escape($guardMessage)) {
    $failures += "C (no nested play\ folder): guard fired on an ordinary checkout. Output:`n$outC"
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL ($($failures.Count)/3):"
    foreach ($f in $failures) { Write-Host "---`n$f" }
    exit 1
}

Write-Host "PASS: play.bat's shared-checkout guard fires exactly when it should (3/3)."
exit 0
