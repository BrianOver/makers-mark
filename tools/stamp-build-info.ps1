# stamp-build-info.ps1 (P2-SCREEN-02) -- shared build-stamp writer for tools/receipt.ps1 and
# tools/shoot.ps1.
#
# WHY THIS EXISTS
# ---------------
# `godot/assets/build_info.txt` is the file `BuildStamp.cs` renders as a corner label in every
# frame -- the "receipt: branch@sha | clean/dirty | date" watermark. Before this script existed,
# only receipt.ps1 wrote it (as part of its rebuild-stamp-reimport-shoot ceremony), and
# shoot.ps1 -- documented and used as a standalone single-state capture tool in its own right,
# not only as a child of receipt.ps1 -- never touched it. So a screenshot taken via shoot.ps1
# alone could carry whatever branch@sha receipt.ps1 last happened to stamp in that worktree,
# not the commit actually being rendered. That already caused a wasted diagnosis (P2-SCREEN-02).
#
# The fix is this one shared function, dot-sourced by both callers, so the stamp is written
# identically (same format, same BOM-less UTF-8 encoding) no matter which script produced it --
# never two copies of the same logic drifting apart.
#
# Usage:
#   . (Join-Path $repo "tools\stamp-build-info.ps1")
#   $stamp = Set-BuildInfoStamp -Repo $repo

function Set-BuildInfoStamp {
    param([Parameter(Mandatory = $true)][string]$Repo)

    $buildInfoPath = Join-Path $Repo "godot\assets\build_info.txt"
    $sha = (git rev-parse --short HEAD)
    $branch = (git rev-parse --abbrev-ref HEAD)
    $dirty = if ((git status --porcelain)) { "dirty" } else { "clean" }
    $dateStr = (Get-Date -Format "yyyy-MM-dd")
    $stamp = "receipt: $branch@$sha | $dirty | $dateStr"
    # Windows PowerShell 5.1's `Set-Content -Encoding utf8` writes a BOM; play.bat's equivalent
    # stamp write does not, and BuildStamp.cs's plain .Trim() would leave a stray BOM character
    # rather than strip it. Write BOM-less UTF-8 explicitly to match that convention exactly.
    [System.IO.File]::WriteAllText($buildInfoPath, $stamp, (New-Object System.Text.UTF8Encoding($false)))
    return $stamp
}
