<#
.SYNOPSIS
    Pure request/reply mechanics for one ollama call (W1, docs/plans/2026-08-10-002 "the playtest
    becomes a player") -- split out of agent-playtest.ps1's own Invoke-Model/JsonEsc so the request
    BODY and the reply LEGALITY check can be proven with hand-built strings, no ollama/Godot/VRAM
    needed. Same shape as this folder's other files (scope-map.ps1, turn-prompt.ps1, ...): the impure
    half (the actual HTTP call) stays in agent-playtest.ps1; everything decidable from strings alone
    moves here, and tools/test-agent-playtest-modes.ps1 dot-sources it directly.

.DESCRIPTION
    Build-ModelRequestBody replaces the inline JSON-string concatenation that used to live directly in
    Invoke-Model. ConvertTo-Json is still banned on this path -- see JsonEsc's own note below: a 2.5 KB
    prompt serialized through Windows PowerShell 5.1's ConvertTo-Json into a 46 MILLION character body
    on 2026-08-04, because it wraps rather than emits nested string content. JsonEsc hand-escapes;
    this file's only addition is splicing the (already-valid, already-compact) action-schema.json text
    straight into the "format" field UNESCAPED -- it is JSON already, and JsonEsc-ing valid JSON would
    double-escape it into a quoted STRING value instead of a schema OBJECT value, which ollama's
    /api/chat would then reject or (worse) silently ignore as an opaque string.

    Get-LegalCommandFromReply replaces the old NORMALIZE block and the '\{[^{}]*\}' regex-extract that
    used to live in agent-playtest.ps1's per-turn loop (both DELETED in the same change that added
    this file). With format=action-schema.json constraining decoding, a reply can no longer arrive as
    prose-wrapping-JSON or a folded verb ("openCounter" instead of press/OpenCounter) -- the grammar
    the schema compiles to cannot emit either shape. What it cannot rule out is a SEMANTIC refusal:
    the model choosing a real verb aimed at a target that is not legal on THIS turn, almost always a
    disabled control. Ruling 1 of the plan is explicit that this must stay uncaught by the schema --
    "the schema must not enum the enabled controls... an illegal press IS signal", the raw material of
    the per-control frustration map a later wave builds. So the honesty counters this drives
    (fallbackTurns/DEGRADED in agent-playtest.ps1) are REDEFINED here, not removed: "three attempts
    produced no LEGAL action" now covers a disabled-control press, an empty reply, a model call that
    threw, or (defensively, in case a model or a test ignores the schema) a reply that still fails to
    parse.

    "eyes learn labels" wave, U1: a model reads the LABEL painted on a control ("Close") but the
    harness only ever accepted the NODE NAME ("CloseLedger") -- found live in a 24-run campaign,
    full/first-timer-1 died on "disabled/absent control: Close" at the exact state first-timer-6
    typed "CloseLedger" and proceeded (median model-driven rate 11% across the campaign). Get-
    LegalCommandFromReply now resolves a press target that misses the NAME list but matches exactly
    ONE enabled control's LABEL (case-insensitively, trimmed) to that control's real name -- the
    caller is told via ResolvedFromLabel/ResolvedToName so it can log the resolution, and the
    returned Command has its target rewritten so a downstream press actually reaches a real node.
    Two or more label matches refuse (naming the candidates); an empty target refuses (naming up to
    5 enabled controls); and a label match is only ever considered among controls that are THEMSELVES
    enabled right now -- ruling 1's "an illegal press IS signal" still holds, so a label can never
    resurrect a disabled control into a legal one.

    "the playtest learns to finish" wave, U1 (owner finding 2026-08-11 + fable census: 58 of 58 model
    runs died on patience by day 3, ~1,190 of ~1,260 refusals were the 8B model emitting semantically
    EMPTY commands -- freeform compose-a-command is beyond the local model). Build-ActMenu/
    Get-LegalCommandFromMenuChoice replace composing JSON with PICKING A NUMBER: the reply contract
    becomes {"choice": <int>, "why": "...", "note": "..."}, and Get-LegalCommandFromReply above is
    RETAINED (not deleted, not called from the main act loop any more) purely because
    Get-ResolvedPressCommandText is reused verbatim by Get-CommandTextFromMenuItem's own press case --
    "reuse the existing command construction, do not fork it" is the plan's own wording. Label
    resolution (the whole point of the eyes-learn-labels wave above) becomes unnecessary under a menu:
    the model never types a name or a label again, only an index, so Format-ControlDescriptor's own
    "name -- label" text is now purely DISPLAY, read by the model to decide which number to send, never
    something it has to reproduce correctly.

    Get-ModelResidencyPlan is U2's own eyes/brain-split decision, made PURE so the unload ordering
    can be proven without ollama/Godot/VRAM -- see its own doc for the VRAM math and the reasoning
    for why split mode skips a per-turn model swap entirely rather than swapping vision/brain in and
    out every turn.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file it is dot-sourced by.
#>

# JSON-escape a string by hand. Moved verbatim from agent-playtest.ps1 -- see this file's own header
# note for why ConvertTo-Json is banned on the request-body path.
function JsonEsc([string]$s) {
    if ($null -eq $s) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($ch -eq "`t") { [void]$sb.Append('\t') }
        elseif ($code -lt 32 -or $code -gt 126) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

# Builds the exact JSON text body agent-playtest.ps1 POSTs to /api/chat. $FormatSchema, when passed, is
# raw JSON schema TEXT (read straight from action-schema.json) spliced in unescaped as the "format"
# field's value. $Temperature, when >= 0, joins $NumCtx inside "options"; left at its default (-1,
# omitted) ollama uses its own per-model sampling default -- the plan's "temperature 0" ruling applies
# to schema-constrained ACT calls specifically, not the warm-up or judge calls, so the caller decides
# per call rather than this function assuming every call wants it.
function Build-ModelRequestBody {
    param(
        [Parameter(Mandatory)][string]$Model,
        [Parameter(Mandatory)][string]$SystemPrompt,
        [Parameter(Mandatory)][string]$UserText,
        [string]$ImageBase64,
        [int]$NumCtx = 8192,
        [string]$FormatSchema,
        [double]$Temperature = -1
    )

    $imagesJson = ''
    if ($ImageBase64) { $imagesJson = ',"images":["' + $ImageBase64 + '"]' }

    $optionsJson = '{"num_ctx":' + $NumCtx
    if ($Temperature -ge 0) { $optionsJson = $optionsJson + ',"temperature":' + $Temperature }
    $optionsJson = $optionsJson + '}'

    $formatJson = ''
    if ($FormatSchema) { $formatJson = ',"format":' + $FormatSchema }

    return '{"model":"' + (JsonEsc $Model) + '","stream":false,"options":' + $optionsJson + $formatJson + ',"messages":[' +
        '{"role":"system","content":"' + (JsonEsc $SystemPrompt) + '"},' +
        '{"role":"user","content":"' + (JsonEsc $UserText) + '"' + $imagesJson + '}]}'
}

# The five real verbs the bridge accepts (AgentPlaytest.cs's AgentCommand + its ActionType switch) --
# duplicated here as a literal rather than dot-sourcing coverage.ps1's registry, because this file must
# work from a bare reply string with no repo-root/state context at all, exactly the shape a mocked test
# reply has.
$script:KnownActionVerbs = @('press', 'move', 'key', 'advance', 'stop')

# "key" targets an InputMap action name, and unlike "press" (whose legal target set changes every
# turn -- ruling 1's whole point) this vocabulary is FIXED by the protocol itself (act.md: "key:
# interact|cancel") and never changes, so enumerating it here does not reintroduce the thing ruling 1
# forbids. Found live (W1, docs/plans/2026-08-10-002 verification): before this list existed, a real
# llava:7b veteran run sent `{"action":"key","target":"","why":"Open the counter..."}` on EVERY one of
# 8 turns -- schema-legal shape, so it passed straight through as "model-driven" while the CLIENT
# refused it 8 times in a row ("no InputMap action named ''") -- 0% fallback ratio reported for a run
# that made literally zero legal progress. That is the exact self-flattery shape A1/A6 exist to catch,
# reopened one level down by the schema migration for this one verb; this closes it.
$script:KnownKeyTargets = @('interact', 'cancel')

# "move" needs a real "dir" the same way "press" needs a real "target" -- fixed vocabulary (act.md:
# "up|down|left|right", plus the "+"-joined composite AgentPlaytest.cs's ParseDirection accepts, e.g.
# "right+down"), mirrored from action-schema.json's own dir enum. Found live in the SAME verification
# pass as $script:KnownKeyTargets: a real qwen3-vl:8b veteran run emitted
# `{"action":"move","why":"moving to the market..."}` with NO "dir" field at all on 3 of 8 turns --
# schema-legal (dir is optional), so it passed straight through as "model-driven" while the client
# refused every one of them ("unknown move dir ''"). Same self-flattery shape, third verb.
$script:KnownMoveDirs = @('up', 'down', 'left', 'right', 'up+left', 'up+right', 'down+left', 'down+right')

# U1 (playtest-finishes wave): a fixed frame count for every menu-chosen move, since the model no
# longer picks its own (the menu offers a direction, not a direction+distance) -- see Build-ActMenu.
# Between act.md's old "~20 frames for a short step" and "~60 to cross a room" suggestions; a model
# that wants to keep walking the same way just picks the same menu item again next turn.
$script:MenuMoveFrames = 40

# One control's display text for the model: the bare NAME when its label is identical, or is the
# "<Name>" bracket placeholder ScreenObservation.ObservedControls emits for a textless button (see
# AgentPlaytest.cs's ControlDigest doc -- that placeholder carries no real information a model could
# read off the rendered screen), else "Name -- label: "Label"" so the word actually painted on screen
# rides alongside the name a press must use. Pure string formatting, no I/O -- shared shape between
# this file's own enabled-controls-for-the-model list and turn-prompt.ps1's per-turn Controls: block
# (duplicated there rather than dot-sourced, same reasoning as $script:KnownActionVerbs above: each
# file must stand alone from a bare stubbed state/reply, no cross-file dependency).
function Format-ControlDescriptor {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Label
    )

    if (-not $Label) { return $Name }
    $trimmedLabel = $Label.Trim()
    if (-not $trimmedLabel) { return $Name }
    if ($trimmedLabel -eq $Name) { return $Name }
    if ($trimmedLabel -eq ('<' + $Name + '>')) { return $Name }
    return ($Name + ' -- label: "' + $trimmedLabel + '"')
}

# The enabled-controls list as agent-playtest.ps1 hands it to the model in REFUSED/STUCK feedback --
# one descriptor per ENABLED control (see Format-ControlDescriptor), in the caller's own array order.
# $Controls is the observation's own controls array (name/label/enabled -- ScreenObservation
# .ObservedControls via AgentPlaytest.cs's ControlDigest); property access below is case-insensitive
# either way PowerShell reads it.
function Get-EnabledControlDescriptors {
    param([array]$Controls)

    $result = New-Object System.Collections.ArrayList
    foreach ($c in @($Controls)) {
        if (-not $c.enabled) { continue }
        if (-not $c.name) { continue }
        [void]$result.Add((Format-ControlDescriptor -Name ([string]$c.name) -Label ([string]$c.label)))
    }
    return ,@($result)
}

# Rebuilds a "press" command's JSON text with its target swapped to $ResolvedTarget -- used only when
# Get-LegalCommandFromReply resolves a LABEL to a control's real name, so the command that actually
# goes out the door presses a node that exists rather than replaying the model's own label text
# straight through (which the client would refuse with "no visible control named '<label>'"). Hand-
# built with JsonEsc (this file's own top-of-file helper), never ConvertTo-Json -- see this file's
# header for why that path is banned here. why/note are carried over verbatim when present; dir/
# frames are never relevant to a press.
function Get-ResolvedPressCommandText {
    param(
        [Parameter(Mandatory)]$ParsedCommand,
        [Parameter(Mandatory)][string]$ResolvedTarget
    )

    $parts = New-Object System.Collections.ArrayList
    [void]$parts.Add('"action":"press"')
    [void]$parts.Add('"target":"' + (JsonEsc $ResolvedTarget) + '"')
    if ($ParsedCommand.why) { [void]$parts.Add('"why":"' + (JsonEsc ([string]$ParsedCommand.why)) + '"') }
    if ($ParsedCommand.note) { [void]$parts.Add('"note":"' + (JsonEsc ([string]$ParsedCommand.note)) + '"') }
    return ('{' + ($parts -join ',') + '}')
}

# Decides whether one model reply is a LEGAL command RIGHT NOW, given this turn's enabled-control list.
# Returns Command=$null/Refused=$true/Reason=<why> on anything short of legal; Command=<the reply text,
# trimmed, or a label-resolved rewrite -- see below>/Refused=$false on success. Never throws -- a reply
# this malformed is exactly the case the caller needs a reason string for, not an exception to catch.
#
# $EnabledControlLabels (U1, eyes-learn-labels): OPTIONAL, defaults to none for backward compatibility
# with every existing caller that only ever passed -EnabledControls. When supplied it is the SAME
# observation controls array Get-EnabledControlDescriptors reads (name/label/enabled) -- used ONLY to
# resolve a press target that missed the plain NAME list against those controls' LABELS. A control is
# never eligible as a label match unless its own name is ALSO in $EnabledControls -- a label can never
# resurrect a disabled control (ruling 1 stays intact: an illegal press is signal, not something to
# silently rewrite into a legal one).
function Get-LegalCommandFromReply {
    param(
        [string]$Reply,
        [string[]]$EnabledControls,
        [array]$EnabledControlLabels = @()
    )

    if (-not $Reply -or -not $Reply.Trim()) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = 'empty reply'; ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    $trimmed = $Reply.Trim()
    $parsed = $null
    try { $parsed = $trimmed | ConvertFrom-Json } catch { $parsed = $null }
    if (-not $parsed -or -not $parsed.action) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = 'reply JSON had no action (schema should prevent this -- treat as a defect if seen live)'; ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    if ($script:KnownActionVerbs -notcontains $parsed.action) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = ('unknown action "' + $parsed.action + '" (schema should prevent this -- treat as a defect if seen live)'); ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    if ($parsed.action -eq 'press') {
        if ($EnabledControls -contains $parsed.target) {
            return [pscustomobject]@{ Command = $trimmed; Refused = $false; Reason = ''; ResolvedFromLabel = $null; ResolvedToName = $null }
        }

        $trimmedTarget = ''
        if ($parsed.target) { $trimmedTarget = ([string]$parsed.target).Trim() }

        if (-not $trimmedTarget) {
            $sample = @($EnabledControls) | Select-Object -First 5
            return [pscustomobject]@{ Command = $null; Refused = $true;
                Reason = ('empty press target -- enabled controls: ' + ($sample -join ', '));
                ResolvedFromLabel = $null; ResolvedToName = $null }
        }

        $labelMatches = New-Object System.Collections.ArrayList
        foreach ($ctrl in @($EnabledControlLabels)) {
            if (-not $ctrl.name -or -not $ctrl.label) { continue }
            if ($EnabledControls -notcontains $ctrl.name) { continue } # never resurrect a disabled control
            if (([string]$ctrl.label).Trim() -ieq $trimmedTarget) { [void]$labelMatches.Add([string]$ctrl.name) }
        }
        $uniqueLabelMatches = @($labelMatches | Select-Object -Unique)

        if ($uniqueLabelMatches.Count -eq 1) {
            $resolvedName = $uniqueLabelMatches[0]
            $resolvedText = Get-ResolvedPressCommandText -ParsedCommand $parsed -ResolvedTarget $resolvedName
            return [pscustomobject]@{ Command = $resolvedText; Refused = $false; Reason = '';
                ResolvedFromLabel = $trimmedTarget; ResolvedToName = $resolvedName }
        }
        if ($uniqueLabelMatches.Count -ge 2) {
            return [pscustomobject]@{ Command = $null; Refused = $true;
                Reason = ('ambiguous label "' + $trimmedTarget + '" matches ' + $uniqueLabelMatches.Count + ' enabled controls: ' + ($uniqueLabelMatches -join ', '));
                ResolvedFromLabel = $null; ResolvedToName = $null }
        }

        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('disabled/absent control: ' + $parsed.target); ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    if ($parsed.action -eq 'key' -and $script:KnownKeyTargets -notcontains $parsed.target) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('illegal key target: "' + $parsed.target + '" (must be interact or cancel)'); ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    if ($parsed.action -eq 'move' -and $script:KnownMoveDirs -notcontains $parsed.dir) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('illegal/missing move dir: "' + $parsed.dir + '" (must be up/down/left/right or a "+"-joined composite)'); ResolvedFromLabel = $null; ResolvedToName = $null }
    }

    return [pscustomobject]@{ Command = $trimmed; Refused = $false; Reason = ''; ResolvedFromLabel = $null; ResolvedToName = $null }
}

# --- U1 (playtest-finishes wave): menu-choice acting -------------------------------------------

# Builds THIS TURN's numbered menu, mechanically, from three sources only -- never a fixed
# vocabulary the model has to compose against:
#   0            -- advance, ALWAYS (law 2: skipping stays legal, no exceptions)
#   1..N         -- one item per ENABLED control, in the observation's own array order (itself
#                   stable turn to turn for the same control set -- it mirrors the game's own UI
#                   tree), display text via Format-ControlDescriptor so a model that can only read
#                   the label painted on screen still knows which number to send
#   (if canMove) -- one item per direction in $script:KnownMoveDirs, in that fixed order
#   (if legal)   -- interact, only when the screen is actually offering it: State.interactPrompt is
#                   non-empty, OR any State.nearby entry reports inRange -- act.md's own words for
#                   interactPrompt ("present only when the game is actually offering you the E key")
#                   are the mechanical legality signal here, extended to nearby.inRange since a
#                   "YOU ARE HERE, press interact" building uses that field instead
#   always       -- cancel, last. No per-turn legality signal exists for it (none ever did -- the
#                   old free-form schema never pre-refused a cancel attempt either, see
#                   Get-LegalCommandFromReply's own $script:KnownKeyTargets check above), so it is
#                   offered unconditionally, exactly as permissive as before this unit.
# Ordering is therefore deterministic given the same inputs -- the same enabled-control set, the
# same canMove/interact legality, always numbers the same way.
function Build-ActMenu {
    param([Parameter(Mandatory)]$State)

    $items = New-Object System.Collections.ArrayList
    [void]$items.Add([pscustomobject]@{
        Index       = 0
        DisplayText = '0. advance -- let the day move on'
        Command     = [pscustomobject]@{ Action = 'advance' }
    })

    $idx = 1
    foreach ($c in @($State.controls)) {
        if (-not $c.enabled) { continue }
        if (-not $c.name) { continue }
        $name = [string]$c.name
        $desc = Format-ControlDescriptor -Name $name -Label ([string]$c.label)
        [void]$items.Add([pscustomobject]@{
            Index       = $idx
            DisplayText = ($idx.ToString() + '. press ' + $desc)
            Command     = [pscustomobject]@{ Action = 'press'; Target = $name }
        })
        $idx++
    }

    if ($State.canMove) {
        foreach ($dir in $script:KnownMoveDirs) {
            [void]$items.Add([pscustomobject]@{
                Index       = $idx
                DisplayText = ($idx.ToString() + '. move ' + $dir)
                Command     = [pscustomobject]@{ Action = 'move'; Dir = $dir }
            })
            $idx++
        }
    }

    $interactLegal = $false
    if ($State.interactPrompt) { $interactLegal = $true }
    foreach ($n in @($State.nearby)) { if ($n.inRange) { $interactLegal = $true } }
    if ($interactLegal) {
        [void]$items.Add([pscustomobject]@{
            Index       = $idx
            DisplayText = ($idx.ToString() + '. interact -- use the thing you are next to')
            Command     = [pscustomobject]@{ Action = 'key'; Target = 'interact' }
        })
        $idx++
    }

    [void]$items.Add([pscustomobject]@{
        Index       = $idx
        DisplayText = ($idx.ToString() + '. cancel -- leave this room or close this panel')
        Command     = [pscustomobject]@{ Action = 'key'; Target = 'cancel' }
    })
    $idx++

    return ,@($items)
}

# Turns one menu item back into the exact command JSON text the OLD free-form path would have built
# for that same verb -- "reuse the existing command construction, do not fork it." $MenuItem is one
# entry from Build-ActMenu's own array (.Command.Action/.Target/.Dir); $Parsed is the model's raw
# reply, already ConvertFrom-Json'd, read only for .why/.note (never for .action/.target -- those
# come from the MENU ITEM the choice resolved to, never from anything the model typed).
#
# "press" reuses Get-ResolvedPressCommandText VERBATIM -- the same helper U1 of the eyes-learn-labels
# wave built to rewrite a label-matched press into its real control name, since building "a press
# command's JSON given a target/why/note" is exactly what a menu choice needs too. advance/move/key
# never had a dedicated builder before (the model composed their JSON itself), so their shape is
# built here directly, but the JSON keys/shape are UNCHANGED from what act.md's old contract produced
# -- no game-side change is needed to accept it.
function Get-CommandTextFromMenuItem {
    param(
        [Parameter(Mandatory)]$MenuItem,
        $Parsed
    )

    if ($MenuItem.Command.Action -eq 'press') {
        return Get-ResolvedPressCommandText -ParsedCommand $Parsed -ResolvedTarget $MenuItem.Command.Target
    }

    $parts = New-Object System.Collections.ArrayList
    [void]$parts.Add('"action":"' + $MenuItem.Command.Action + '"')
    if (($MenuItem.Command.PSObject.Properties.Name -contains 'Target') -and $MenuItem.Command.Target) {
        [void]$parts.Add('"target":"' + (JsonEsc $MenuItem.Command.Target) + '"')
    }
    if (($MenuItem.Command.PSObject.Properties.Name -contains 'Dir') -and $MenuItem.Command.Dir) {
        [void]$parts.Add('"dir":"' + $MenuItem.Command.Dir + '"')
        [void]$parts.Add('"frames":' + $script:MenuMoveFrames)
    }
    if ($Parsed -and $Parsed.why) { [void]$parts.Add('"why":"' + (JsonEsc ([string]$Parsed.why)) + '"') }
    if ($Parsed -and $Parsed.note) { [void]$parts.Add('"note":"' + (JsonEsc ([string]$Parsed.note)) + '"') }
    return ('{' + ($parts -join ',') + '}')
}

# Decides whether one model reply is a LEGAL menu pick RIGHT NOW, given $MenuItems (Build-ActMenu's
# own output for this turn). Same Command/Refused/Reason shape as Get-LegalCommandFromReply, on
# purpose, so the caller's honesty counters (fallbackTurns/DEGRADED) do not need to change meaning:
# "three attempts produced no legal action" now covers an empty reply, a missing/non-integer choice,
# or an out-of-range one -- an out-of-range choice IS STILL SIGNAL (ruling 1's own words), it still
# drains the patience meter exactly like an illegal press used to. The kernel can still reject the
# resulting command outright (a menu is what the SCREEN offers; legality of the OUTCOME stays the
# game's own answer, never pre-empted here) -- that rejection still lands in the backend log exactly
# as before, this function has no opinion on it.
function Get-LegalCommandFromMenuChoice {
    param(
        [string]$Reply,
        [array]$MenuItems
    )

    if (-not $Reply -or -not $Reply.Trim()) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = 'empty reply' }
    }

    $trimmed = $Reply.Trim()
    $parsed = $null
    try { $parsed = $trimmed | ConvertFrom-Json } catch { $parsed = $null }
    if ((-not $parsed) -or (-not ($parsed.PSObject.Properties.Name -contains 'choice')) -or ($null -eq $parsed.choice) -or (([string]$parsed.choice).Trim() -eq '')) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = 'reply JSON had no choice (schema should prevent this -- treat as a defect if seen live)' }
    }

    $choiceText = ([string]$parsed.choice).Trim()
    $choice = 0
    if (-not [int]::TryParse($choiceText, [ref]$choice)) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = ('choice was not an integer: "' + $choiceText + '"') }
    }

    $match = @($MenuItems | Where-Object { $_.Index -eq $choice })
    if ($match.Count -eq 0) {
        $maxIndex = 0
        if (@($MenuItems).Count -gt 0) { $maxIndex = (@($MenuItems) | Measure-Object -Property Index -Maximum).Maximum }
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = ('out-of-range choice ' + $choice + ' -- valid range is 0 to ' + $maxIndex) }
    }

    $commandText = Get-CommandTextFromMenuItem -MenuItem $match[0] -Parsed $parsed
    return [pscustomobject]@{ Command = $commandText; Refused = $false; Reason = '' }
}

# --- U2 (playtest-finishes wave): eyes/brain residency plan -------------------------------------

# Pure decision, so the unload ordering can be proven without ollama/Godot/VRAM. $BrainModel empty
# means single-model mode (today's exact behaviour, kept for A/B): $Model narrates AND chooses, the
# judge pass calls $JudgeModel separately, with the existing unload-$Model-before/unload-$JudgeModel-
# after dance (ruling 10, docs/plans/2026-08-10-002).
#
# $BrainModel non-empty means SPLIT mode. The VRAM math (agent-playtest.ps1's own .PARAMETER
# BrainModel doc): qwen3-vl:8b (~6.1 GB) + qwen3:14b (~9.3 GB) sum to ~15.4 GB, over this project's
# ~14 GB ceiling if both stayed resident across a turn -- so split mode does NOT swap vision and
# brain in and out every turn (that would cost a model load/unload twice per turn, over the whole
# run's turn budget, for no benefit worth the wall-clock). Instead $Model (vision) is never loaded
# at all in split mode: frame narration is SKIPPED, not swapped -- ActUsesImage=false tells the
# caller to send no image and rely on the state digest + screen text + menu, which already carry
# the observable facts a model needs to choose. $BrainModel alone is resident for BOTH the per-turn
# choice call and the judge pass (JudgeModel=BrainModel, UnloadBeforeJudge empty -- nothing to
# unload, the judge reuses the SAME already-warm model), so split mode costs exactly ONE model load
# and ONE unload for the whole run -- fewer swaps than single-model mode's own act-then-judge
# handoff, not more.
function Get-ModelResidencyPlan {
    param(
        [Parameter(Mandatory)][string]$Model,
        [string]$BrainModel = '',
        [Parameter(Mandatory)][string]$JudgeModel
    )

    $splitMode = [bool]$BrainModel

    if ($splitMode) {
        return [pscustomobject]@{
            SplitMode         = $true
            ActModel          = $BrainModel
            ActUsesImage      = $false
            JudgeModel        = $BrainModel
            UnloadBeforeJudge = @()
            UnloadAfterRun    = @($BrainModel)
        }
    }

    return [pscustomobject]@{
        SplitMode         = $false
        ActModel          = $Model
        ActUsesImage      = $true
        JudgeModel        = $JudgeModel
        UnloadBeforeJudge = @($Model)
        UnloadAfterRun    = @($JudgeModel)
    }
}
