<#
.SYNOPSIS
  Block until a PR's checks are terminal, then exit 0 only if every check the branch ruleset
  actually requires passed.

.DESCRIPTION
  The overnight loop needs to know whether a PR may merge. There are four wrong ways to answer
  that and this script exists to avoid all of them:

  1. A `while ($true) { gh pr checks; Start-Sleep 30 }` poll. This repo's own CI standards forbid
     arbitrary waits, and a shell poll is the shape that trips a harness's content-analysis
     approval prompt -- which stalls an unattended run at 3am waiting for a human who is asleep.
     `gh pr checks --watch` is gh's own blocking primitive: one subprocess, refreshing internally,
     no sleep.

  2. `gh pr checks --required`. That filters to checks GitHub marks required *by branch
     protection*, which is empty on an unprotected feature branch -- so it prints "no required
     checks reported" and exits non-zero. A false red, on every PR.

  3. A hardcoded list of job names. That is the defect this repo keeps paying for: a fact written
     by hand in a second place, correct the day it was typed. Add a required check and a hardcoded
     list silently stops gating on it. So the required contexts are READ LIVE from the ruleset on
     every run, and the script refuses outright if that read comes back empty -- an empty required
     set means protection was removed or the API call failed, and both of those are "do not
     merge", never "nothing to check".

  4. Trusting `gh`'s own exit code. `--watch` exits non-zero when ANY check failed, including
     checks nothing requires (a preview build, an optional lint). Reading that as the gate is
     CLAUDE.md rule 10's own named defect -- a wrapper computing a verdict from an exit code -- so
     the exit status of `--watch` is discarded and the buckets are re-read and compared by name.

  Exit 0 = every required context passed. Exit 1 = at least one did not. Exit 2 = the script
  refuses to answer (bad argument, unreadable ruleset, unreadable checks). Fail closed in all three.

  This script never merges anything. It reports.

.PARAMETER Pr
  The pull request number. A bare positive integer -- no URLs, branches or flags.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/loop/ci-watch.ps1 -Pr 748
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Pr
)

$ErrorActionPreference = 'Stop'

function Deny([string]$message) {
    Write-Host "BLOCKED: $message Refusing -- fail closed."
    exit 2
}

if ($Pr -notmatch '^[1-9][0-9]*$') {
    Deny "'$Pr' is not a positive PR number."
}

# Every gh read below goes through ConvertFrom-Json rather than gh's own --jq. Not a style choice:
# Windows PowerShell 5.1's native-command argument parser eats the double quotes inside a jq
# program, so `select(.enforcement == "active")` reaches gh as `select(.enforcement == active)` and
# dies with "function not defined: active/0". Measured, not guessed -- that was this script's first
# draft. Parsing in PowerShell has no such seam.
function GhJson([string[]]$ghArgs) {
    $raw = & gh @ghArgs 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) { return $null }
    try { return ($raw | ConvertFrom-Json) } catch { return $null }
}

$repoInfo = GhJson @('repo', 'view', '--json', 'nameWithOwner')
if ($null -eq $repoInfo) { Deny "could not resolve the repository from this checkout." }
$repo = $repoInfo.nameWithOwner

# Live required contexts, from every ACTIVE ruleset on this repo. Deliberately NOT filtered by
# ref-name pattern: a ruleset can target the default branch by the ~DEFAULT_BRANCH token or by a
# literal name, and a filter that quietly matches neither reads exactly like "no protection" --
# the one failure this script must never mistake for a pass. The union over active rulesets can
# only make the gate stricter, never looser.
$rulesets = GhJson @('api', "repos/$repo/rulesets")
if ($null -eq $rulesets) { Deny "could not read $repo's rulesets." }

$required = New-Object System.Collections.Generic.List[string]
foreach ($summary in @($rulesets)) {
    if ($summary.enforcement -ne 'active') { continue }
    $ruleset = GhJson @('api', "repos/$repo/rulesets/$($summary.id)")
    if ($null -eq $ruleset) { continue }
    foreach ($rule in @($ruleset.rules)) {
        if ($rule.type -ne 'required_status_checks') { continue }
        foreach ($check in @($rule.parameters.required_status_checks)) {
            if ($check.context) { $required.Add($check.context) }
        }
    }
}

$required = @($required | Sort-Object -Unique)
if ($required.Count -eq 0) {
    Deny "$repo publishes no active required status checks. That is either lost protection or a failed API read, and both mean do-not-merge."
}

Write-Host "Required by $repo's ruleset: $($required -join ', ')"
Write-Host "Blocking on PR #$Pr until every check is terminal..."

# --watch blocks until terminal. Its exit status is deliberately discarded (see 4 above).
& gh pr checks $Pr --watch --interval 30 *> $null

$rows = GhJson @('pr', 'checks', $Pr, '--json', 'name,bucket')
if ($null -eq $rows) { Deny "could not read any check state for PR #$Pr. Treat as not-green." }

$buckets = @{}
foreach ($row in @($rows)) { $buckets[$row.name] = $row.bucket }

$failed = @()
foreach ($context in $required) {
    $bucket = if ($buckets.ContainsKey($context)) { $buckets[$context] } else { 'missing' }
    if ($bucket -ne 'pass') { $failed += "$context=$bucket" }
}

if ($failed.Count -gt 0) {
    Write-Host "CI not green for PR #$($Pr) -- do not merge. Required checks not passing: $($failed -join ', ')"
    exit 1
}

Write-Host "CI green for PR #$($Pr) -- every required check passed ($($required -join ', '))."
exit 0
