<#
.SYNOPSIS
    Pure logic for U1 (playtest-harness wave): keep every frame the model saw, not just the last one.

.DESCRIPTION
    Today frame.png is one file, overwritten every turn by AgentPlaytestBridge.RunLoop -- so an
    80-turn run leaves exactly ONE screenshot on disk, of the last turn, which is usually the least
    interesting one (idle Evening screen, not the moment something broke). This file is the archiving
    half: which turns to keep (-FrameEvery thinning), what to name the kept copy, and how to make
    turnlog.md say so per turn rather than leaving a reader to guess which frame a note is about.

    Split out for the same reason completion.ps1/scope-map.ps1/turn-prompt.ps1 are: zero Godot, zero
    ollama, zero VRAM to prove. Dot-source this file to test it in isolation.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# The kept-frame filename for a given turn -- zero-padded to 3 digits so a plain directory listing
# sorts chronologically (turn-001.png .. turn-999.png) without a human needing to sort by date.
# Turns past 999 are not expected (Turns defaults to 40; nothing in this harness runs an 80-turn
# budget past three digits), but D3 formatting degrades gracefully to a wider string rather than
# throwing, so a freak long run still produces a valid, just no-longer-3-wide, filename.
function Get-KeptFrameFileName {
    param([Parameter(Mandatory)][int]$Turn)
    return ('turn-' + $Turn.ToString('D3') + '.png')
}

# Whether turn $Turn's frame should be KEPT under -FrameEvery thinning. (($Turn-1) % FrameEvery) -eq 0
# rather than ($Turn % FrameEvery) -eq 0 so turn 1 is always kept (a run that stops after turn 1 must
# not have thinned away its only frame) and the kept count is exactly ceil(N/FrameEvery) for N turns
# -- verified turn by turn: FrameEvery=5 keeps 1,6,11,... which is ceil(N/5) for every N, where the
# turn-modulo variant would instead keep 5,10,... i.e. floor(N/5), silently dropping the last partial
# group. FrameEvery<1 is a caller error; clamp to 1 (keep everything) rather than divide by zero.
function Test-ShouldKeepFrame {
    param(
        [Parameter(Mandatory)][int]$Turn,
        [int]$FrameEvery = 1
    )
    $every = $FrameEvery
    if ($every -lt 1) { $every = 1 }
    return ((($Turn - 1) % $every) -eq 0)
}

# Archives one turn's frame.png, or explains why it did not. Returns a pscustomobject the caller
# both ACTS on (Kept -> bump the kept-frame counter for the findings header) and WRITES into the
# turnlog (Note -- see Add-FrameReferencesToTurnLog below), so "kept", "thinned away on purpose",
# and "missing" are three distinct, always-present outcomes rather than "copied" vs "silently
# nothing" -- the asymmetry PR #420 exists to prevent (an imageless turn that left no trace at all).
#
# -SourceMissing is the caller's own knowledge from the model-call path (Invoke-Model's
# $imageMissingThisTurn -- it already detected frame.png was absent when it tried to attach it to
# the request). This function ALSO checks Test-Path itself, so a Scripted-mode caller (which never
# sets that flag, since it never calls Invoke-Model at all) still gets an honest answer.
function Save-TurnFrame {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$FramesDir,
        [Parameter(Mandatory)][int]$Turn,
        [int]$FrameEvery = 1,
        [bool]$SourceMissing = $false
    )

    $fileName = Get-KeptFrameFileName -Turn $Turn
    $relativePath = 'frames/' + $fileName

    if ($SourceMissing -or -not (Test-Path $SourcePath)) {
        return [pscustomobject]@{
            Kept         = $false
            Missing      = $true
            FileName     = $fileName
            RelativePath = $relativePath
            Note         = ('frame missing at turn ' + $Turn + ' -- no frame.png was available to keep ' +
                '(the model saw no image this turn either, if this was a model-driven turn)')
        }
    }

    if (-not (Test-ShouldKeepFrame -Turn $Turn -FrameEvery $FrameEvery)) {
        return [pscustomobject]@{
            Kept         = $false
            Missing      = $false
            FileName     = $fileName
            RelativePath = $relativePath
            Note         = ('frame: not kept at turn ' + $Turn + ' (-FrameEvery ' + $FrameEvery +
                ' thinning -- see the nearest kept frame in frames/)')
        }
    }

    if (-not (Test-Path $FramesDir)) {
        New-Item -ItemType Directory -Path $FramesDir -Force | Out-Null
    }
    Copy-Item -Path $SourcePath -Destination (Join-Path $FramesDir $fileName) -Force

    return [pscustomobject]@{
        Kept         = $true
        Missing      = $false
        FileName     = $fileName
        RelativePath = $relativePath
        Note         = ('frame: ' + $relativePath)
    }
}

# Rewrites turnlog.md's text so every "## Turn N" block carries a line about its frame -- inserted
# right after the block's own "- frame: captured/BLANK" line (AgentPlaytestBridge.RunLoop already
# writes one) when that line exists, or appended at the end of the block when it does not (the
# command-timeout branch writes a turn header with no frame line at all).
#
# Pure text transform, deliberately: turnlog.md itself is rewritten WHOLESALE by the Godot client on
# every flush (FlushLog does File.WriteAllText from its own in-memory StringBuilder, which knows
# nothing about lines this script adds), so annotating it mid-run would be erased on the client's very
# next flush. This must only ever be called ONCE, after the client process has fully exited.
function Add-FrameReferencesToTurnLog {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$TurnLogText,
        [Parameter(Mandatory)][hashtable]$FrameNoteByTurn
    )

    if (-not $TurnLogText) { return $TurnLogText }

    $lines = $TurnLogText -split "`r?`n"
    $out = New-Object System.Collections.ArrayList
    $currentTurn = $null
    $inserted = $false

    foreach ($line in $lines) {
        $headerMatch = [regex]::Match($line, '^##\s+Turn\s+(\d+)\s*$')
        if ($headerMatch.Success) {
            # Flush the PREVIOUS block's note now if it never found a "- frame:" line to ride along
            # with (the timeout branch's own turn header has no such line).
            if (($null -ne $currentTurn) -and (-not $inserted) -and $FrameNoteByTurn.ContainsKey($currentTurn)) {
                [void]$out.Add('- ' + $FrameNoteByTurn[$currentTurn])
            }
            $currentTurn = [int]$headerMatch.Groups[1].Value
            $inserted = $false
            [void]$out.Add($line)
            continue
        }

        [void]$out.Add($line)

        if (($line -match '^- frame:') -and ($null -ne $currentTurn) -and (-not $inserted)) {
            if ($FrameNoteByTurn.ContainsKey($currentTurn)) {
                [void]$out.Add('- ' + $FrameNoteByTurn[$currentTurn])
                $inserted = $true
            }
        }
    }

    # End of text: flush the LAST block's note if it never got one.
    if (($null -ne $currentTurn) -and (-not $inserted) -and $FrameNoteByTurn.ContainsKey($currentTurn)) {
        [void]$out.Add('- ' + $FrameNoteByTurn[$currentTurn])
    }

    return ($out -join [Environment]::NewLine)
}
