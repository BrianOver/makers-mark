<#
.SYNOPSIS
    Pure logic for W3 (docs/plans/2026-08-10-002 "the playtest becomes a player"): the dead-verb
    detector -- a mechanical "this press changed nothing" check that supersedes the sceptic persona
    (ruling 6). Runs under every persona; it is not a mode of its own.

.DESCRIPTION
    Law 3 (CLAUDE.md's seven laws) says every verb changes an outcome or reveals the player's stake.
    A verb that does neither is a candidate defect, and this file is the mechanical half of catching
    one: a whole-state fingerprint of state.json taken immediately before a "press" command and again
    once the next state.json (that press's own outcome) has landed, plus a per-turn slice of the same
    backend log U2 already turned into evidence (backend.ps1). Identical fingerprint AND a backend
    slice that shows no sim event fired is the ONLY shape this file will call a candidate -- and even
    then it is a CANDIDATE for a human to confirm, never asserted as a real defect (ruling 7's exact
    words). Either signal alone proves nothing: a fingerprint can be unchanged while an off-screen
    sim event still fired (say, a background hero tick), and a sim event can fire for a reason
    unrelated to the press just made.

    Ruling 7's fingerprint is EXCLUSION-listed, never inclusion-listed: state.json's fields are walked
    generically (whatever AgentPlaytest.cs's StateDigest happens to carry today), and only the two
    fields that necessarily change on every single turn regardless of whether the press did anything
    -- "turn" (the driver's own counter) and "lastOutcome" (always describes the PRECEDING command) --
    are removed before hashing. This is the same lesson the state-fingerprint incident already taught
    this repo once: a hand-typed field set silently reads as a game bug the day a new field ships and
    nobody remembers to add it to the list. A test in tools/test-agent-playtest-modes.ps1 pins this by
    adding an arbitrary new field to a stub state and asserting the fingerprint changes -- proof the
    walk is generic, not a second hand-typed list wearing a different name.

    Canonicalization matters because two semantically-identical state.json payloads must never hash
    differently just because .NET's JSON parser (or the client's own JSON writer) happened to emit
    object keys in a different order this run. Object keys are sorted alphabetically at every level
    before hashing; ARRAY order is left alone on purpose (screenText/controls/nearby order is real
    game state -- which control is first, what the player is closest to -- never a set to normalize
    away).

    The backend-log half needs a PER-TURN slice, and Get-BackendSummary (backend.ps1) only ever
    answers for the WHOLE run -- see backend.ps1's own Get-BackendEventsForSlice (added by this same
    unit) for why that extension lives there instead of being duplicated here.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# Ruling 7's exclusion list -- EXACTLY these two fields, never a broader hand-typed set. Exposed as a
# script-scope variable (the same pattern personas.ps1 uses for $script:KnownPersonas /
# $script:GameNounAllowlist) so a test can assert on the literal set, not just on behavior that
# happens to match it today.
$script:DeadVerbExcludedFields = @('turn', 'lastOutcome')

# Canonical (sorted-keys, stable) JSON-shaped text for one value, recursively. Not real JSON output
# for a caller to parse -- only ever hashed -- so this optimizes for "two equivalent states always
# produce the same bytes," never for readability or round-tripping.
function ConvertTo-CanonicalJsonText {
    param($Value)

    if ($null -eq $Value) { return 'null' }

    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }

    if ($Value -is [string]) {
        return (ConvertTo-Json -InputObject $Value -Compress)
    }

    # Every JSON number ConvertFrom-Json can produce (Int32/Int64/Double, occasionally Decimal) is a
    # primitive value type in .NET except Decimal, which is not IsPrimitive but still needs the same
    # invariant-culture text form (never the current locale's decimal separator -- CLAUDE.md rule 4's
    # cross-OS determinism concern, even though this runs in tools/, not the sim, the same drift risk
    # applies to any hash meant to compare identically across machines).
    if ($Value.GetType().IsPrimitive -or ($Value -is [decimal])) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $names = @($Value.Keys) | Sort-Object
        $parts = @()
        foreach ($name in $names) {
            $parts += ((ConvertTo-Json -InputObject ([string]$name) -Compress) + ':' +
                (ConvertTo-CanonicalJsonText -Value $Value[$name]))
        }
        return '{' + ($parts -join ',') + '}'
    }

    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $names = @($Value.PSObject.Properties | ForEach-Object { $_.Name }) | Sort-Object
        $parts = @()
        foreach ($name in $names) {
            $parts += ((ConvertTo-Json -InputObject $name -Compress) + ':' +
                (ConvertTo-CanonicalJsonText -Value $Value.$name))
        }
        return '{' + ($parts -join ',') + '}'
    }

    # Arrays/lists -- ORDER PRESERVED. Checked after string/dictionary/PSCustomObject (all three can
    # themselves be enumerable in .NET) so this branch only ever catches real sequences: controls[],
    # nearby[], screenText[].
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) { $items += (ConvertTo-CanonicalJsonText -Value $item) }
        return '[' + ($items -join ',') + ']'
    }

    # Fallback -- nothing state.json actually produces should reach here, but a stringified, still
    # hash-sensitive representation beats throwing mid-run over a value shape nobody anticipated.
    return (ConvertTo-Json -InputObject ([string]$Value) -Compress)
}

# The whole-state fingerprint (ruling 7): every field of $State except $script:DeadVerbExcludedFields,
# canonically serialized and SHA256-hashed. Accepts either a PSCustomObject (the real shape
# state.json's raw text becomes via ConvertFrom-Json) or a Hashtable/ordered dictionary (convenient
# for hand-built test fixtures) -- both walk every property/key present, never a fixed list of names,
# so a field StateDigest grows tomorrow is fingerprinted for free today.
function Get-StateFingerprint {
    param([Parameter(Mandatory)]$State)

    $trimmed = [ordered]@{}
    if ($State -is [System.Collections.IDictionary]) {
        foreach ($key in $State.Keys) {
            if ($script:DeadVerbExcludedFields -contains $key) { continue }
            $trimmed[$key] = $State[$key]
        }
    } else {
        foreach ($prop in $State.PSObject.Properties) {
            if ($script:DeadVerbExcludedFields -contains $prop.Name) { continue }
            $trimmed[$prop.Name] = $prop.Value
        }
    }

    $canonical = ConvertTo-CanonicalJsonText -Value $trimmed
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
        $hashBytes = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }
    return ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
}

# The final decision (ruling 7): a CANDIDATE only when BOTH the fingerprint held still and the
# backend's own per-turn slice (Get-BackendEventsForSlice, backend.ps1) shows no sim event. Either
# side alone is not evidence of a dead verb -- a fingerprint can hold while an off-screen event still
# fired, and a logged event can be unrelated to the press just made -- so this requires both signals
# to agree, and the caller is a game bug ONLY the human reading findings.md gets to decide, never this
# function. $BackendSlice being $null (no backend log at all -- MM_PLAYTEST_LOG unset, or the log
# never opened) is treated as "cannot confirm silence," not as silence itself: this file adds nothing
# it cannot support, mirroring backend.ps1's own "everything below is UNKNOWN, not clean" posture for
# an absent log.
function Get-DeadVerbVerdict {
    param(
        [Parameter(Mandatory)][string]$FingerprintBefore,
        [Parameter(Mandatory)][string]$FingerprintAfter,
        $BackendSlice,
        [Parameter(Mandatory)][int]$Turn,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$ControlName
    )

    $fingerprintUnchanged = ($FingerprintBefore -eq $FingerprintAfter)

    $backendSilent = $false
    if ($null -ne $BackendSlice) { $backendSilent = (-not $BackendSlice.SawSimEvent) }

    $isCandidate = $fingerprintUnchanged -and $backendSilent

    $line = $null
    if ($isCandidate) {
        $line = ('CANDIDATE (law-3, dead verb): turn ' + $Turn + ', phase ' + $Phase +
            ', control "' + $ControlName + '" -- the whole-state fingerprint was identical before ' +
            'and after the press, and the backend log recorded no sim event in that window. Labeled ' +
            'CANDIDATE for human confirmation, never asserted as a defect (ruling 7).')
    }

    return [pscustomobject]@{
        IsCandidate           = $isCandidate
        FingerprintUnchanged  = $fingerprintUnchanged
        BackendSilent         = $backendSilent
        Line                  = $line
    }
}

# --- Frame retention (Definition of Done: "keep that turn's frame regardless of -FrameEvery") ------
#
# Save-TurnFrame (frames.ps1) decides KEPT/thinned for a press turn using only what is known AT that
# turn -- but whether the press turns out to be a dead-verb CANDIDATE is only known one turn later
# (Get-DeadVerbVerdict needs the NEXT state.json, the press's own "after" side). By the time that
# next turn's state has arrived, the client has already overwritten frame.png with ITS OWN
# screenshot, so a frame -FrameEvery thinned away cannot be recovered from source afterward -- it has
# to be staged onto disk while it is still there, and only kept for real once the verdict is in.
# Save-TurnFrame's own copy already covers turns -FrameEvery keeps anyway; these two functions only
# ever run for a press turn it did NOT keep, as a one-turn safety net in case it turns out to matter.

# Copies frame.png to a provisional holding path. Returns $false without staging (never throws) if
# the source frame is not actually there -- the same "missing is reported, not fatal" contract every
# frame helper in this harness already keeps (see Save-TurnFrame's own SourceMissing branch).
function Save-ProvisionalDeadVerbFrame {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$StagingPath
    )
    if (-not (Test-Path $SourcePath)) { return $false }
    $dir = Split-Path -Parent $StagingPath
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -Path $SourcePath -Destination $StagingPath -Force
    return $true
}

# Resolves a staged provisional frame once the verdict is known: promoted into frames/ under its
# normal turn-NNN.png name if the turn WAS a dead-verb candidate, deleted otherwise -- a
# non-candidate turn's frame was correctly thinned by -FrameEvery, and staging it was only ever a
# safety net against the one-turn-late verdict, never a second, permanent copy.
function Resolve-ProvisionalDeadVerbFrame {
    param(
        [Parameter(Mandatory)][string]$StagingPath,
        [Parameter(Mandatory)][string]$FinalPath,
        [Parameter(Mandatory)][bool]$IsCandidate
    )
    if (-not (Test-Path $StagingPath)) { return $false }
    if ($IsCandidate) {
        $dir = Split-Path -Parent $FinalPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Move-Item -Path $StagingPath -Destination $FinalPath -Force
        return $true
    }
    Remove-Item -Path $StagingPath -Force -ErrorAction SilentlyContinue
    return $false
}
