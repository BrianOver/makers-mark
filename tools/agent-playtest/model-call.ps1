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

# Decides whether one model reply is a LEGAL command RIGHT NOW, given this turn's enabled-control list.
# Returns Command=$null/Refused=$true/Reason=<why> on anything short of legal; Command=<the reply text,
# trimmed>/Refused=$false on success. Never throws -- a reply this malformed is exactly the case the
# caller needs a reason string for, not an exception to catch.
function Get-LegalCommandFromReply {
    param(
        [string]$Reply,
        [string[]]$EnabledControls
    )

    if (-not $Reply -or -not $Reply.Trim()) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = 'empty reply' }
    }

    $trimmed = $Reply.Trim()
    $parsed = $null
    try { $parsed = $trimmed | ConvertFrom-Json } catch { $parsed = $null }
    if (-not $parsed -or -not $parsed.action) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = 'reply JSON had no action (schema should prevent this -- treat as a defect if seen live)' }
    }

    if ($script:KnownActionVerbs -notcontains $parsed.action) {
        return [pscustomobject]@{ Command = $null; Refused = $true;
            Reason = ('unknown action "' + $parsed.action + '" (schema should prevent this -- treat as a defect if seen live)') }
    }

    if ($parsed.action -eq 'press' -and $EnabledControls -notcontains $parsed.target) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('disabled/absent control: ' + $parsed.target) }
    }

    if ($parsed.action -eq 'key' -and $script:KnownKeyTargets -notcontains $parsed.target) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('illegal key target: "' + $parsed.target + '" (must be interact or cancel)') }
    }

    if ($parsed.action -eq 'move' -and $script:KnownMoveDirs -notcontains $parsed.dir) {
        return [pscustomobject]@{ Command = $null; Refused = $true; Reason = ('illegal/missing move dir: "' + $parsed.dir + '" (must be up/down/left/right or a "+"-joined composite)') }
    }

    return [pscustomobject]@{ Command = $trimmed; Refused = $false; Reason = '' }
}
