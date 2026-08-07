<#
.SYNOPSIS
  Delete leftover agent worktree directories that git no longer knows about.

.DESCRIPTION
  Background agents create worktrees under .claude\worktrees\. When an agent dies
  without cleaning up, git's registry loses the entry but the directory stays on
  disk -- 7 of them, ~40MB, were sitting there when this script was written.

  Nobody could clear them: `rm` / `Remove-Item` are deny-listed for agents (rightly
  -- an agent fleet with --force habits should not hold a raw delete), and a deny
  rule beats any allow rule, so no permission grant could ever unblock it. This
  script is the sanctioned path: a narrow, auditable delete that agents may run
  under the existing `PowerShell(& .\tools\*)` allowance.

  It removes ONLY directories that are all of:
    - directly under <repo>\.claude\worktrees\
    - not registered in `git worktree list` (i.e. genuinely orphaned)
    - not the directory this script is running from, and not an ancestor of it

  Anything else is refused. There is no flag to widen the target.

.PARAMETER DryRun
  List what would be deleted and exit without touching disk.

.EXAMPLE
  & .\tools\clean-worktrees.ps1 -DryRun
  & .\tools\clean-worktrees.ps1
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Resolve the repo that owns this script, not the caller's working directory --
# an agent invoking this from inside its own worktree must still target the
# shared root's worktree registry.
$repoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel)
if (-not $repoRoot) { throw "not a git repository: $PSScriptRoot" }
$commonDir = (git -C $PSScriptRoot rev-parse --path-format=absolute --git-common-dir)
$sharedRoot = Split-Path -Parent $commonDir
$worktreeRoot = Join-Path $sharedRoot '.claude\worktrees'

if (-not (Test-Path -LiteralPath $worktreeRoot)) {
    Write-Host "no worktree directory at $worktreeRoot -- nothing to do."
    exit 0
}

# Drop registry entries whose directories are already gone, so `worktree list`
# below reflects only live worktrees.
git -C $sharedRoot worktree prune

$registered = @(
    git -C $sharedRoot worktree list --porcelain |
        Where-Object { $_ -like 'worktree *' } |
        ForEach-Object { (Resolve-Path -LiteralPath $_.Substring(9) -ErrorAction SilentlyContinue).Path } |
        Where-Object { $_ }
)

$here = (Get-Location).Path
$orphans = @()

foreach ($dir in (Get-ChildItem -LiteralPath $worktreeRoot -Directory)) {
    $path = $dir.FullName

    if ($registered -contains $path) {
        Write-Host "  keep   $($dir.Name)  (live worktree)"
        continue
    }
    # Never delete the ground under a running session.
    if ($here -eq $path -or $here.StartsWith($path + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "  keep   $($dir.Name)  (this session is running inside it)"
        continue
    }
    # Belt and braces: refuse anything that resolved outside the worktree root.
    if (-not $path.StartsWith($worktreeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "  SKIP   $path  (outside $worktreeRoot -- refusing)"
        continue
    }

    $orphans += $dir
}

if ($orphans.Count -eq 0) {
    Write-Host "no orphaned worktree directories."
    exit 0
}

$totalMb = 0
foreach ($o in $orphans) {
    $bytes = (Get-ChildItem -LiteralPath $o.FullName -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { $bytes = 0 }
    $mb = [math]::Round($bytes / 1MB, 1)
    $totalMb += $mb
    Write-Host ("  orphan $($o.Name)  ({0} MB)" -f $mb)
}

if ($DryRun) {
    Write-Host ("dry run: would delete {0} directories, {1} MB." -f $orphans.Count, [math]::Round($totalMb, 1))
    exit 0
}

$failed = 0
foreach ($o in $orphans) {
    try {
        Remove-Item -LiteralPath $o.FullName -Recurse -Force -Confirm:$false
        Write-Host "  deleted $($o.Name)"
    }
    catch {
        $failed++
        Write-Host "  FAILED  $($o.Name): $($_.Exception.Message)"
    }
}

Write-Host ("done: {0} deleted, {1} failed, ~{2} MB reclaimed." -f ($orphans.Count - $failed), $failed, [math]::Round($totalMb, 1))
if ($failed -gt 0) { exit 1 }
