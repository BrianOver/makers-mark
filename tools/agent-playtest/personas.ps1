<#
.SYNOPSIS
    Pure logic for U4 (playtest-harness wave): five players, not one player five times.

.DESCRIPTION
    One act.md persona ("curious, slightly impatient") drove every run of the last sweep, so thirty
    runs measured the same person thirty times. act.md now keeps only PROTOCOL (the JSON contract,
    movement rules, the never-press-a-disabled-control rule) behind a literal `{{PERSONA}}` marker;
    each file under prompts/personas/ supplies KNOWLEDGE and GOAL only, and this file is the glue:
    resolving -Persona to a real file (failing LOUDLY on an unknown name -- this repo has already
    fixed the silent-fallback shape twice, see agent-playtest.ps1's own A1/A6 notes, and a typo'd
    persona name silently becoming the default would be exactly that defect a third time), and
    stamping the assembled prompt with a short hash so two runs claiming to be different players can
    actually be checked against each other.

    CORRECTION (design review, same wave): act.md's ORIGINAL first draft still taught the game --
    "you play a BLACKSMITH NPC... heroes go raid a mine... craft gear, stock a shop, post bounties,
    serve customers at a counter" in its opening line, and a rule naming `VigilStop` by name and
    telling the model what to do about it. That is the builder wearing a name tag: a first-timer
    persona layered on top of a protocol that already answers "what is this game" is not a
    first-timer, and a protocol that tells every persona how to handle the vigil guarantees no run
    can ever discover it unaided -- the exact copy question the game most needs answered. Fixed by
    moving both into the KNOWLEDGEABLE personas only (veteran/completionist/sceptic get the vigil
    rule; first-timer and speedrunner do not, since the speedrunner mashing through the vigil blind
    is the only honest test that skipping stays legal). Get-GameNounDenylist/Test-TextForGameNouns
    below are the mechanical guard against this regressing a second time.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

$script:KnownPersonas = @('first-timer', 'veteran', 'speedrunner', 'completionist', 'sceptic')

# Resolves -Persona into one of the five real names. "random" picks one FOR THIS RUN via $Random
# (overridable so a test can assert on the choice instead of trusting Get-Random); anything else
# must match a known persona exactly. An unrecognized value THROWS -- never silently becomes a
# default persona, which would read as "first-timer" right up until someone noticed the findings
# never once mentioned the veteran-only mid-game.
function Resolve-PersonaChoice {
    param(
        [Parameter(Mandatory)][string]$Persona,
        [scriptblock]$Random = { param($items) $items | Get-Random }
    )

    if ($Persona -eq 'random') {
        return (& $Random $script:KnownPersonas)
    }

    if ($script:KnownPersonas -notcontains $Persona) {
        throw ('unknown persona "' + $Persona + '". Valid: ' + ($script:KnownPersonas -join ', ') + ', random.')
    }

    return $Persona
}

# The final act-prompt text: act.md's protocol with the literal `{{PERSONA}}` marker replaced by the
# chosen persona file's own text. Throws (never silently drops the persona half) if act.md lost its
# marker or the persona file cannot be read -- both are configuration errors, not soft conditions.
#
# String .Replace(), not -replace: a persona file's text is untrusted-enough (hand-authored prose)
# that a stray `$0`/backreference-looking substring must never be reinterpreted as regex replacement
# syntax the way PowerShell's -replace operator would.
function Build-PersonaActPrompt {
    param(
        [Parameter(Mandatory)][string]$ActProtocolText,
        [Parameter(Mandatory)][string]$PersonaName,
        [Parameter(Mandatory)][string]$PersonasDir
    )

    if ($ActProtocolText -notmatch '\{\{PERSONA\}\}') {
        throw 'act.md is missing its {{PERSONA}} marker -- cannot assemble a persona-specific prompt.'
    }

    $personaPath = Join-Path $PersonasDir ($PersonaName + '.md')
    if (-not (Test-Path $personaPath)) {
        throw ('persona file not found: ' + $personaPath)
    }

    $personaText = (Get-Content $personaPath -Raw).Trim()
    return $ActProtocolText.Replace('{{PERSONA}}', $personaText)
}

# A short, stable hash of the assembled prompt -- put in findings.md's header alongside the persona
# name, per the brief, "so two runs claiming to be different players can be checked." 12 hex chars
# (48 bits) is plenty to catch "these two runs used the same act.md and someone forgot to change
# -Persona" without needing the full 64-char SHA256 in a header line meant to be skimmed.
function Get-PromptHash {
    param([Parameter(Mandatory)][string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }
    $hex = [System.BitConverter]::ToString($hashBytes) -replace '-', ''
    return $hex.Substring(0, 12).ToLowerInvariant()
}

# --- Noun-purity guard (design-review correction) ------------------------------------------------

# Derives the "this is a game noun" denylist from THE-GAME.md's OWN glossary table (docs/design/
# THE-GAME.md, "## 8. Glossary") rather than hand-typing one -- a hand-typed list is the exact defect
# shape this repo keeps re-finding (CLAUDE.md rules 6-10, this file's own header note): it silently
# rots the moment the glossary gains a term and nobody remembers to mirror it into a second list.
#
# A leading "The "/"the " article is stripped ("The mark" -> "mark", "The Foundry" -> "Foundry") --
# the glossary writes it that way as running prose, but the bare noun is the thing that would
# actually leak into a prompt. "Standing / favour" names two separate single-word terms sharing one
# row, not one compound phrase, so it splits on " / " into two entries.
function Get-GameNounDenylist {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Join-Path $RepoRoot 'docs\design\THE-GAME.md'
    # Leading comma on every return below -- see coverage.ps1's own ARRAY-RETURN NOTE. This list is
    # ~16 entries today so would not presently trigger the single-element unwrap, but the file-missing
    # and no-glossary-found branches return EMPTY arrays, which need the comma to avoid becoming $null.
    if (-not (Test-Path $path)) { return ,@() }
    $text = Get-Content $path -Raw

    $sectionMatch = [regex]::Match($text, '(?s)##\s*8\.\s*Glossary(.*?)(\r?\n##\s|\z)')
    if (-not $sectionMatch.Success) { return ,@() }
    $section = $sectionMatch.Groups[1].Value

    $terms = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($section -split "`r?`n")) {
        # Table rows look like: | **Term** | Meaning text... |
        $m = [regex]::Match($line, '^\|\s*\*\*(.+?)\*\*\s*\|')
        if (-not $m.Success) { continue }
        $term = $m.Groups[1].Value.Trim()
        $term = $term -replace '(?i)^the\s+', ''
        foreach ($part in ($term -split '\s*/\s*')) {
            $p = $part.Trim()
            if ($p) { [void]$terms.Add($p) }
        }
    }

    return ,@($terms | Select-Object -Unique)
}

# Curated exemptions for a glossary term that ALSO occurs as ordinary English -- each entry needs a
# one-line justification so a future addition cannot silently widen the allowlist without a reviewer
# seeing why it is safe. Empty today: the stripped act.md and first-timer.md do not happen to need
# any of the glossary's plain-English lookalikes (e.g. "mark" as a plain verb, "park" as a plain
# verb). Left as a named, tested seam rather than deleted -- the day one IS needed, it is one line
# here with a reason, not a loosened regex nobody can trace back to why.
$script:GameNounAllowlist = @(
    # 'mark' -- would be needed if the interface text ever used "mark this turn as done" or similar;
    # not currently used, so not exempted. Add here (with the actual sentence that needs it) if that
    # changes, rather than pre-exempting a collision that does not exist yet.
)

# Every denylist term (minus any curated $Allowlist entry) that appears in $Text as a whole word or
# phrase, case-insensitive. Empty result = clean. This is the mechanical proof behind "act.md and
# first-timer.md teach no game-specific vocabulary" -- the exact property a first-timer persona
# depends on (a first-timer that already knows the glossary is the builder wearing a name tag) and
# the one act.md's original draft violated.
function Test-TextForGameNouns {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][array]$Denylist,
        [array]$Allowlist = @()
    )

    $allowSet = @{}
    foreach ($a in $Allowlist) { $allowSet[$a.ToLowerInvariant()] = $true }

    $hits = @()
    foreach ($term in $Denylist) {
        if ($allowSet.ContainsKey($term.ToLowerInvariant())) { continue }
        $pattern = '(?i)\b' + [regex]::Escape($term) + '\b'
        if ($Text -match $pattern) { $hits += $term }
    }
    # Leading comma -- THIS is the return value a caller most often checks with `.Count -eq 0`, and a
    # single-hit failure (exactly the case a regression is likeliest to produce first) is exactly the
    # case bare `@($hits)` would have silently misreported. See coverage.ps1's ARRAY-RETURN NOTE.
    return ,@($hits)
}
