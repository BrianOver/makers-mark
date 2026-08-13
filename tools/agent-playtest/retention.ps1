<#
.SYNOPSIS
    Pure logic for runs/ retention (fix/the-pilot-plays-like-a-person): keep the campaign's
    conclusions, prune the screenshots that dwarf them.

.DESCRIPTION
    Why this exists: a single archived tools/playtest-sweep.ps1 campaign directory (runs/playtest/
    <stamp>/<tag>/frames/*.png, one PNG per kept turn across every run in the sweep) reached 750MB on
    disk, and nothing in this repo ever prunes it -- runs/ is gitignored (.gitignore:39), so this is
    pure, unbounded local disk waste, not a git problem. The owner's own ask: "clear out as you go."

    DELETION SAFETY IS THE HIGHEST BAR HERE, not a footnote. This file only ever plans and removes ONE
    THING: a directory literally named "frames" directly inside a directory this file itself already
    confirmed looks like one run's own output (it must contain at least one of that output's known
    artifact files -- state.json/turnlog.md/findings.md/run-meta.json/metrics.json/backend.json/
    coverage.json/playtest-log.jsonl/notes.md -- never a bare "there is a folder called frames
    somewhere" match). Everything else in a run directory (findings.md, turnlog.md, metrics.json,
    run-meta.json -- the small text that carries the actual conclusions) is never touched by this
    file at all. Three independent guards must all agree before anything is even PLANNED for removal:

      1. ROOT CONTAINMENT (Test-PathIsUnderRoot) -- the candidate's own resolved absolute path must
         start with the caller-supplied, resolved $RunsRoot's own path. Pure string/path logic, no git
         dependency, so it works identically against a real checkout's runs/ directory and a synthetic
         fixture directory under $env:TEMP (this file's own tests use ONLY the latter, never the real
         runs/, per this repo's own testing rule for exactly this kind of code).

      2. KEEP-NEWEST FLOOR (Get-PlaytestRetentionPlan's own $KeepNewest) -- the N most-recently-active
         run directories (by the newest file write time found anywhere under them, not by directory
         name or creation time, since a sweep's own campaign folder name is a start-of-run timestamp
         that says nothing about which of ITS runs finished last) are marked keep-recent and never
         planned for pruning, regardless of size. Default is conservative on purpose (5) -- a too-eager
         prune eating a run someone still needed is a worse outcome than 750MB sitting on a disk.

      3. MIN-AGE FLOOR (Get-PlaytestRetentionPlan's own $MinAgeMinutes) -- a run directory whose newest
         file write is younger than this is marked keep-too-recent even if it fell outside the
         keep-newest floor above (a second, independent net for "this might still be the run currently
         being written", never relying on the keep-newest count alone to catch it).

    The caller (tools/playtest-sweep.ps1) adds a FOURTH guard on top of these three, at the point it
    actually calls Invoke-PlaytestRetentionPrune for real: a one-time `git check-ignore --quiet` on the
    resolved runs root itself (never per-file -- runs/ is a single gitignore line with no negation, so
    one directory-level check covers every path beneath it) before ever calling this file's own prune
    function, and refuses to prune at all if that check fails or git is unavailable. That check lives
    in the driver, not here, because it depends on a real git repository existing, which this file's
    own unit tests deliberately do not set up (see the DESCRIPTION above).

    DEFAULT ACTION IS PRUNE-FRAMES-ONLY, never delete a whole run directory. "A run's conclusions
    should survive its screenshots" (the owner's own framing) -- findings.md/turnlog.md/metrics.json/
    run-meta.json are bytes; frames/ is the bulk. A run whose frames/ is already empty or absent is
    simply not a pruning candidate at all (nothing to free, nothing to report).

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# The run-artifact filenames this file trusts as "this directory is genuinely one run's own output",
# not just any folder that happens to contain a "frames" subdirectory. Kept as a list (not a single
# required name) because different scopes/personas/driver versions can produce a different subset --
# see agent-playtest.ps1's own $stale array for where this same set of names comes from.
$script:PlaytestRunArtifactNames = @(
    'state.json', 'turnlog.md', 'findings.md', 'run-meta.json', 'metrics.json',
    'backend.json', 'coverage.json', 'playtest-log.jsonl', 'notes.md'
)

# Resolved-path containment: $Path is $Root itself or strictly nested under it. Both are resolved via
# [System.IO.Path]::GetFullPath so "..", mixed slash direction, and a missing trailing separator all
# normalize the same way before the string comparison -- deliberately NOT Resolve-Path, which throws on
# a path that does not exist yet (a candidate this function is asked to judge always exists by the time
# it is called here, but keeping this on plain string/path math means it never needs the filesystem at
# all, which is what makes it cheap to call for every candidate).
function Test-PathIsUnderRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if ($fullPath -ieq $fullRoot) { return $true }
    return $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

# Every file's LastWriteTimeUtc under $Path, recursively -- $null (never an exception, never "now")
# when $Path does not exist or holds no files at all, so a caller can tell "no files here" apart from
# "somehow written at the epoch".
function Get-NewestWriteTimeUtc {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path $Path)) { return $null }
    $newest = $null
    Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ((-not $newest) -or ($_.LastWriteTimeUtc -gt $newest)) { $newest = $_.LastWriteTimeUtc }
    }
    return $newest
}

# Total bytes and file count under $Path, recursively -- both 0 when $Path does not exist or is empty,
# never a divide-by-zero or a null-reference further up the call chain.
function Get-DirectoryStats {
    param([Parameter(Mandatory)][string]$Path)

    $stats = [pscustomobject]@{ SizeBytes = 0; FileCount = 0 }
    if (-not (Test-Path $Path)) { return $stats }
    Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        $stats.SizeBytes += $_.Length
        $stats.FileCount++
    }
    return $stats
}

# Bytes as a human-readable string (KB/MB/GB) for the console report -- 750MB of frames should not be
# reported back as "786432000 bytes freed".
function Format-ByteSize {
    param([Parameter(Mandatory)][double]$Bytes)

    if ($Bytes -ge 1GB) { return ([math]::Round($Bytes / 1GB, 2).ToString() + ' GB') }
    if ($Bytes -ge 1MB) { return ([math]::Round($Bytes / 1MB, 2).ToString() + ' MB') }
    if ($Bytes -ge 1KB) { return ([math]::Round($Bytes / 1KB, 2).ToString() + ' KB') }
    return ([math]::Round($Bytes, 0).ToString() + ' B')
}

# Every directory under $RunsRoot that looks like one run's own output: it directly contains a
# "frames" subdirectory AND at least one of $script:PlaytestRunArtifactNames sitting next to it. Both
# conditions must hold -- a "frames" folder alone (some unrelated tool's own cache, hypothetically)
# is never enough on its own to mark a directory as prunable.
function Find-PlaytestRunDirectories {
    param([Parameter(Mandatory)][string]$RunsRoot)

    $found = New-Object System.Collections.ArrayList
    if (-not (Test-Path $RunsRoot)) { return ,@($found) }

    Get-ChildItem -Path $RunsRoot -Recurse -Directory -Filter 'frames' -ErrorAction SilentlyContinue | ForEach-Object {
        $runDir = $_.Parent.FullName
        $hasArtifact = $false
        foreach ($name in $script:PlaytestRunArtifactNames) {
            if (Test-Path (Join-Path $runDir $name)) { $hasArtifact = $true; break }
        }
        if ($hasArtifact) {
            [void]$found.Add([pscustomobject]@{ RunDir = $runDir; FramesDir = $_.FullName })
        }
    }
    return ,@($found)
}

# The plan: for every run directory Find-PlaytestRunDirectories can see under $RunsRoot, one row
# saying exactly what will happen to it and WHY -- "silent deletion is its own failure shape in this
# project" applies just as much to silent KEEPS as silent prunes; a caller printing this plan should
# never have to guess why a given run was spared.
#
# $ProtectPaths -- resolved and matched by Test-PathIsUnderRoot-style exact-or-nested comparison
# against each candidate's OWN RunDir, so a caller can name either the exact run directory or an
# ancestor of it (e.g. the sweep's own $stampDir currently being written) and either still protects it.
function Get-PlaytestRetentionPlan {
    param(
        [Parameter(Mandatory)][string]$RunsRoot,
        [int]$KeepNewest = 5,
        [int]$MinAgeMinutes = 30,
        [string[]]$ProtectPaths = @(),
        [datetime]$NowUtc = (Get-Date).ToUniversalTime()
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RunsRoot)
    $resolvedProtect = @($ProtectPaths | Where-Object { $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_) })

    $candidates = Find-PlaytestRunDirectories -RunsRoot $resolvedRoot
    $rows = New-Object System.Collections.ArrayList
    foreach ($c in $candidates) {
        $newest = Get-NewestWriteTimeUtc -Path $c.RunDir
        $ageMinutes = [double]::PositiveInfinity
        if ($newest) { $ageMinutes = ($NowUtc - $newest).TotalMinutes }

        $stats = Get-DirectoryStats -Path $c.FramesDir
        $protectedByPath = $false
        foreach ($p in $resolvedProtect) {
            if (Test-PathIsUnderRoot -Path $c.RunDir -Root $p) { $protectedByPath = $true; break }
            if (Test-PathIsUnderRoot -Path $p -Root $c.RunDir) { $protectedByPath = $true; break }
        }

        [void]$rows.Add([pscustomobject]@{
            RunDir        = $c.RunDir
            FramesDir     = $c.FramesDir
            NewestWriteUtc= $newest
            AgeMinutes    = $ageMinutes
            SizeBytes     = $stats.SizeBytes
            FileCount     = $stats.FileCount
            ProtectedPath = $protectedByPath
            Action        = 'prune' # provisional; finalized below
        })
    }

    # Newest-first by actual last activity (never by directory name/stamp -- see this file's own
    # header for why a campaign folder's start-of-run timestamp is the wrong proxy for "which of its
    # runs finished most recently").
    $sorted = @($rows | Sort-Object -Property @{ Expression = { $_.NewestWriteUtc }; Descending = $true })

    for ($i = 0; $i -lt $sorted.Count; $i++) {
        $row = $sorted[$i]
        if ($row.ProtectedPath) {
            $row.Action = 'keep-protected'
        } elseif ($i -lt $KeepNewest) {
            $row.Action = 'keep-recent'
        } elseif ($row.AgeMinutes -lt $MinAgeMinutes) {
            $row.Action = 'keep-too-recent'
        } elseif ($row.FileCount -eq 0) {
            $row.Action = 'keep-empty'
        } else {
            $row.Action = 'prune'
        }
    }

    return ,@($sorted)
}

# The ONLY function in this file that deletes anything, and the only thing it ever deletes is a row's
# own FramesDir -- never RunDir, never anything a caller passes in that this file did not itself
# discover via Find-PlaytestRunDirectories above. Re-verifies containment under $RunsRoot at the
# moment of deletion (never trusts that the plan was built moments ago against the same filesystem
# state) and re-checks Action -eq 'prune' -- a caller that mutated the plan's Action field by hand
# before calling this gets exactly the safety that field's own value describes, nothing more.
function Invoke-PlaytestRetentionPrune {
    param(
        [Parameter(Mandatory)][string]$RunsRoot,
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$Plan,
        [switch]$WhatIf
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RunsRoot)
    $results = New-Object System.Collections.ArrayList

    foreach ($row in $Plan) {
        if ($row.Action -ne 'prune') { continue }
        if (-not (Test-PathIsUnderRoot -Path $row.FramesDir -Root $resolvedRoot)) {
            [void]$results.Add([pscustomobject]@{
                RunDir = $row.RunDir; Removed = $false; SizeBytes = 0; FileCount = 0
                Note = 'REFUSED: ' + $row.FramesDir + ' does not resolve under ' + $resolvedRoot
            })
            continue
        }
        if (-not (Test-Path $row.FramesDir)) {
            [void]$results.Add([pscustomobject]@{
                RunDir = $row.RunDir; Removed = $false; SizeBytes = 0; FileCount = 0
                Note = 'already gone: ' + $row.FramesDir
            })
            continue
        }

        if ($WhatIf) {
            [void]$results.Add([pscustomobject]@{
                RunDir = $row.RunDir; Removed = $false; SizeBytes = $row.SizeBytes; FileCount = $row.FileCount
                Note = 'would prune (WhatIf): ' + $row.FramesDir
            })
            continue
        }

        try {
            Remove-Item -Path $row.FramesDir -Recurse -Force -ErrorAction Stop
            [void]$results.Add([pscustomobject]@{
                RunDir = $row.RunDir; Removed = $true; SizeBytes = $row.SizeBytes; FileCount = $row.FileCount
                Note = 'pruned: ' + $row.FramesDir
            })
        } catch {
            [void]$results.Add([pscustomobject]@{
                RunDir = $row.RunDir; Removed = $false; SizeBytes = 0; FileCount = 0
                Note = 'FAILED to prune ' + $row.FramesDir + ': ' + $_.Exception.Message
            })
        }
    }

    return ,@($results)
}

# One console line per plan row (never silent about a keep, per this repo's own "silent deletion is
# its own failure shape" rule) plus a final total -- the caller (tools/playtest-sweep.ps1) prints this
# verbatim rather than re-deriving its own summary from the plan.
function Get-PlaytestRetentionReportLines {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$Plan,
        [array]$PruneResults = @()
    )

    $lines = New-Object System.Collections.ArrayList
    if ($Plan.Count -eq 0) {
        [void]$lines.Add('retention: no playtest run directories found -- nothing to consider.')
        return ,@($lines)
    }

    foreach ($row in $Plan) {
        $sizeText = Format-ByteSize -Bytes $row.SizeBytes
        $tag = Split-Path -Leaf $row.RunDir
        [void]$lines.Add('retention: ' + $tag + ' -- ' + $row.Action + ' (' + $row.FileCount + ' frame file(s), ' + $sizeText + ')')
    }

    $prunedResults = @($PruneResults | Where-Object { $_.Removed })
    $totalBytes = 0
    $totalFiles = 0
    foreach ($r in $prunedResults) { $totalBytes += $r.SizeBytes; $totalFiles += $r.FileCount }
    [void]$lines.Add('retention: pruned ' + $prunedResults.Count + ' run(s), freed ' + (Format-ByteSize -Bytes $totalBytes) + ' (' + $totalFiles + ' file(s)).')

    $failed = @($PruneResults | Where-Object { -not $_.Removed -and $_.Note -like 'FAILED*' })
    if ($failed.Count -gt 0) {
        [void]$lines.Add('retention: ' + $failed.Count + ' prune attempt(s) FAILED -- see the notes above.')
    }

    return ,@($lines)
}
