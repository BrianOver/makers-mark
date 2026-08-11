<#
.SYNOPSIS
    S3 (scripted-deep-pilot lane): the model stops piloting and starts judging. Reads a COMPLETED
    deep run's own artifacts (turnlog + friction log + six-decisions ledger + frame count) and asks
    a local reasoning model three questions in one pass: is this fun, does it match the game idea,
    and where were the dead stretches.

.DESCRIPTION
    Different shape from every judge call tools/agent-playtest.ps1 makes: that script judges a run it
    just finished playing, live, with a $turnRecords array already built in memory. This script judges
    a run that already ended -- typically tools/agent-playtest.ps1 -Persona pilot's own output
    directory, but any completed run with a turnlog.md works. It never launches Godot and never plays
    anything; it is a pure read of what a run already left on disk plus one ollama call.

    Reuses agent-playtest.ps1's own judge plumbing rather than inventing new machinery: the SAME
    Build-ModelRequestBody request-building (model-call.ps1), the SAME fabrication guard shape (every
    quoted SCREAMING_CASE token must appear in what the model actually read), and the SAME standing
    "what this instrument cannot see" footer (footer.ps1) -- with one extra line naming this pass as
    text-only (the critic model never sees a screenshot; frames on disk are counted as corroborating
    evidence for a human, never analyzed). prompts/critic.md is built FROM scout-judge.md's own
    THE-GAME.md framing (the five links/six decisions/two observable laws) plus judge.md's
    quote-everything discipline, with one deliberate addition scout-judge.md's own rules refuse on
    purpose: a real fun verdict. A short scouting run has not earned one (scout-judge.md's rule 2
    says so explicitly); a 150+-turn run that reached day 11 has.

    GPU: qwen3:14b is ~9.3 GB. Same >=8GB-free gate as agent-playtest.ps1's own -MinFreeGb default,
    same resident-model exemption (a model already loaded is not a second job), same >83C ceiling,
    and the SAME symmetric unload after the call (#452's rule) so the next GPU-gated run on this
    machine starts clean -- `ollama stop`, stderr left unredirected (the PS 5.1 2>&1 trap this whole
    tool family already documents).

.PARAMETER RunDir
    Path to a completed agent-playtest run's artifact directory (contains turnlog.md at minimum).

.PARAMETER Model
    ollama model tag for the critic pass. Default qwen3:14b -- the same dedicated text model
    agent-playtest.ps1's own judge pass uses, for the same reason (no image is ever sent here).

.PARAMETER OutFile
    Where to write the verdict. Default <RunDir>/critic.md.

.EXAMPLE
    .\tools\deep-run-critic.ps1 -RunDir .claude\agent-playtest-pilot-150

.EXAMPLE
    .\tools\deep-run-critic.ps1 -RunDir runs\pilot-seed7 -Model qwen3:14b -MinFreeGb 8

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??, matching every file in this tool family.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunDir,
    [string]$Model = 'qwen3:14b',
    [string]$Endpoint = 'http://127.0.0.1:11434',
    [int]$MinFreeGb = 8,
    [int]$MaxTempC = 83,
    [int]$NumCtx = 16384,
    [string]$OutFile,
    [int]$MaxDigestChars = 32000
)

. (Join-Path $PSScriptRoot 'agent-playtest\model-call.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\footer.ps1')
. (Join-Path $PSScriptRoot 'agent-playtest\critic.ps1')

$ErrorActionPreference = 'Stop'

function Say($text)  { Write-Host ('deep-run-critic: ' + $text) -ForegroundColor Cyan }
function Warn($text) { Write-Host ('deep-run-critic: ' + $text) -ForegroundColor Yellow }
function Die($lines) {
    Write-Host ''
    Write-Host ('DEEP-RUN-CRITIC REFUSED: ' + $lines[0]) -ForegroundColor Red
    if ($lines.Count -gt 1) {
        foreach ($line in $lines[1..($lines.Count - 1)]) { Write-Host $line -ForegroundColor Red }
    }
    exit 1
}

if (-not (Test-Path $RunDir)) {
    Die @(('-RunDir does not exist: ' + $RunDir))
}
$RunDir = (Resolve-Path $RunDir).Path
if (-not $OutFile) { $OutFile = Join-Path $RunDir 'critic.md' }

$artifacts = $null
try {
    $artifacts = Read-CriticRunArtifacts -RunDir $RunDir
} catch {
    Die @($_.Exception.Message)
}

$digest = Get-DeepRunDigest -RawTurnLog $artifacts.FullLog -MaxChars $MaxDigestChars
Say ('digest: ' + $digest.DayCount + ' day(s), ' + $digest.Text.Length + ' chars (thinned: ' + $digest.Thinned + ')')
Say ('friction entries: ' + @($artifacts.FrictionEntries).Count + ', six-decision entries: ' +
    @($artifacts.DecisionEntries).Count + ', frames on disk: ' + $artifacts.FrameCount)

# --- GPU gate: same precondition as agent-playtest.ps1's own, never a hope ----------------------
$smi = & nvidia-smi --query-gpu=memory.total,memory.used,temperature.gpu --format=csv,noheader,nounits 2>&1
if ($LASTEXITCODE -ne 0) {
    Die @('nvidia-smi failed, so the GPU state is unknown. Refusing to load a model blind.')
}
$parts = ($smi | Select-Object -First 1) -split ','
$totalMb = [int]$parts[0].Trim()
$usedMb  = [int]$parts[1].Trim()
$tempC   = [int]$parts[2].Trim()
$freeGb  = [math]::Round(($totalMb - $usedMb) / 1024.0, 1)
Say ('GPU: ' + $freeGb + ' GB free, ' + $tempC + ' C')

$resident = @()
try {
    $ps = Invoke-RestMethod -Uri ($Endpoint + '/api/ps') -TimeoutSec 10
    $resident = @($ps.models | ForEach-Object { $_.name })
} catch { }

if ($resident -contains $Model) {
    Say ($Model + ' is already loaded, so the free-VRAM floor does not apply to it')
} elseif ($freeGb -lt $MinFreeGb) {
    Die @(
        ('only ' + $freeGb + ' GB VRAM free, floor is ' + $MinFreeGb + ' GB.'),
        ('Resident models: ' + ($resident -join ', ')),
        'Project rule: never risk the machine. Free it first (ollama stop <model>, close ComfyUI).'
    )
}
if ($tempC -gt $MaxTempC) {
    Die @(('GPU is at ' + $tempC + ' C, ceiling is ' + $MaxTempC + ' C. Let it cool.'))
}

$tags = try { Invoke-RestMethod -Uri ($Endpoint + '/api/tags') -TimeoutSec 10 } catch { $null }
if (-not $tags) {
    Die @(('ollama is not reachable at ' + $Endpoint + '.'), 'Start it (ollama serve) and retry.')
}
$have = @($tags.models | ForEach-Object { $_.name })
if ($have -notcontains $Model) {
    Die @(
        ('model ' + $Model + ' is not pulled. Available: ' + ($have -join ', ')),
        ('Pull it (ollama pull ' + $Model + ') or pass -Model with one of the above.')
    )
}

# --- The critic call -------------------------------------------------------------------------
$criticPromptPath = Join-Path $PSScriptRoot 'agent-playtest\prompts\critic.md'
if (-not (Test-Path $criticPromptPath)) {
    Die @(('critic prompt not found: ' + $criticPromptPath))
}
$criticPrompt = Get-Content $criticPromptPath -Raw

$frictionLines = Format-CriticFrictionLines -Entries $artifacts.FrictionEntries
$decisionLines = Format-CriticDecisionLines -Entries $artifacts.DecisionEntries

$userText = @(
    ('This deep run''s artifacts live at: ' + $RunDir),
    ('Frames captured on disk (NOT visually analyzed -- this is a text-only pass, see the footer): ' + $artifacts.FrameCount),
    '',
    '## Per-day digest of the turn log',
    '',
    $digest.Text,
    '',
    '## Friction log (on-screen text/refusal copy quoted verbatim by the run itself)',
    ''
) + $frictionLines + @(
    '',
    '## Six decisions this run took',
    ''
) + $decisionLines

$userTextJoined = $userText -join [Environment]::NewLine

Say 'asking the critic model'
$body = Build-ModelRequestBody -Model $Model -SystemPrompt $criticPrompt -UserText $userTextJoined -NumCtx $NumCtx -Temperature -1
$bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
$verdict = ''
try {
    $resp = Invoke-RestMethod -Uri ($Endpoint + '/api/chat') -Method Post -Body $bytes -ContentType 'application/json' -TimeoutSec 300
    $verdict = $resp.message.content
} catch {
    $detail = ''
    if ($_.ErrorDetails) { $detail = ' :: ' + $_.ErrorDetails.Message }
    Warn ('ollama ' + $_.Exception.Message + $detail + ' (body was ' + $body.Length + ' chars)')
}

# #452's symmetric-unload rule: the critic model must not ride into the next GPU-gated run on this
# machine. Unredirected stderr on purpose -- `ollama stop`'s own progress spinner writes to stderr
# even on success, and PS 5.1's `2>&1` would wrap that into a terminating NativeCommandError.
try { & ollama stop $Model | Out-Null } catch { Warn ('ollama stop ' + $Model + ' failed: ' + $_.Exception.Message) }
Say ($Model + ' unloaded -- the next GPU-gated run starts clean')

if (-not $verdict) {
    Die @('the critic call produced nothing. Nothing was written to ' + $OutFile + '.')
}

# --- Fabrication guard: the same discipline as agent-playtest.ps1's own end-of-run check --------
$haystack = $artifacts.FullLog + [Environment]::NewLine + ($frictionLines -join [Environment]::NewLine) +
    [Environment]::NewLine + ($decisionLines -join [Environment]::NewLine)
$unsupported = Get-CriticUnsupportedTokens -VerdictText $verdict -Haystack $haystack

$guardNote = @()
if ($unsupported.Count -gt 0) {
    Write-Host ''
    Warn ('FABRICATION GUARD: ' + $unsupported.Count + ' quoted token(s) appear nowhere in this run''s own artifacts:')
    foreach ($t in $unsupported) { Warn ('  ' + $t + '  <- not seen; treat this claim as invented until a human confirms it') }
    $guardNote = @(
        '',
        '## Fabrication guard',
        '',
        ('These tokens are quoted in the verdict above but appear NOWHERE in the turnlog/friction-log/' +
         'six-decisions text this critic pass actually read:'),
        ''
    ) + ($unsupported | ForEach-Object { '- `' + $_ + '`' })
}

$header = @(
    "# Maker's Mark -- deep-run critic verdict (S3)",
    '',
    ('- run: ' + $RunDir),
    ('- generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
    ('- critic model: ' + $Model),
    ('- digest: ' + $digest.DayCount + ' day(s), ' + $digest.Text.Length + ' chars (thinned: ' + $digest.Thinned + ')'),
    ('- friction entries read: ' + @($artifacts.FrictionEntries).Count),
    ('- six-decision entries read: ' + @($artifacts.DecisionEntries).Count),
    ('- frames on disk (not visually analyzed): ' + $artifacts.FrameCount),
    ''
)

$footerLines = Get-HonestyFooterLines -ExtraLines @(
    ('- **This is a text-only pass** -- the critic model never saw a screenshot. Frames on disk are ' +
     'corroborating evidence for a human to spot-check, never something this verdict is grounded in.')
)

Set-Content -Path $OutFile -Encoding utf8 -Value ($header + @($verdict) + $guardNote + $footerLines)
Say ('critic verdict written: ' + $OutFile)
Write-Host ''
Write-Host $verdict
Write-Host ''
if ($unsupported.Count -gt 0) {
    Warn 'At least one claim quotes text this run never produced -- see the fabrication guard above.'
    exit 1
}
exit 0
