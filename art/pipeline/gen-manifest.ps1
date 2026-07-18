<#
gen-manifest.ps1 -- generate godot/assets/art/art-manifest.json from committed PNGs.

Stage 4 of the Maker's Mark art pipeline (generate -> cutout -> normalmap -> MANIFEST).
Scans godot/assets/art/*.png, derives an asset id per file (strip a trailing "_n" normal-map
suffix, strip ".png"), and emits a JSON manifest of

    { "<id>": { "diffuse": true, "normal": <bool> }, ... }

with ordinal-sorted keys so regenerations produce clean, deterministic diffs. This is the U3
(P006, R10) presence manifest AssetCatalog.Has/HasNormal (via IconRegistry) read -- generated
from committed pixels, never from GameState (R14).

Local dev tooling only -- never wired into CI as a generation step. -Check is a drift guard:
it recomputes the on-disk id set and compares it against the committed manifest without
writing anything, non-zero exit on any difference.

Windows PowerShell 5.1 compatible (no PS7-only syntax) -- run with either `pwsh` or
`powershell.exe`.

Usage:
    pwsh art/pipeline/gen-manifest.ps1            # (re)write the manifest
    pwsh art/pipeline/gen-manifest.ps1 --check    # drift guard, exit non-zero on mismatch
    pwsh art/pipeline/gen-manifest.ps1 --help

Mirrors normalmap.py's voice: plain args, a "prog: error: msg" die on stderr, non-zero exit.
#>

function Show-Usage {
    Write-Output 'usage: gen-manifest.ps1 [--check] [--help]'
    Write-Output ''
    Write-Output 'Generate godot/assets/art/art-manifest.json by scanning godot/assets/art/*.png.'
    Write-Output ''
    Write-Output '  --check   do not write; exit non-zero if the on-disk PNG set differs from'
    Write-Output '            the committed manifest (drift guard for CI/pre-commit).'
    Write-Output '  --help    show this message and exit 0.'
}

function Die {
    param([string]$GenManifestMessage, [int]$GenManifestCode = 1)
    [Console]::Error.WriteLine("gen-manifest.ps1: error: $GenManifestMessage")
    exit $GenManifestCode
}

# ---- arg parsing (plain $args -- no param() block, so "--check" binds as a literal token) -----
$Check = $false
foreach ($arg in $args) {
    switch -Regex ($arg) {
        '^(--check|-check)$' { $Check = $true }
        '^(--help|-help|-h)$' { Show-Usage; exit 0 }
        default { Die "unknown argument: $arg (see --help)" }
    }
}

# ---- paths (resolved from the script's own location -- works from any CWD) --------------------
$PipelineDir = $PSScriptRoot
$ArtDir = Split-Path -Parent (Split-Path -Parent $PipelineDir)
$ArtDir = Join-Path $ArtDir 'godot\assets\art'
$ManifestPath = Join-Path $ArtDir 'art-manifest.json'

if (-not (Test-Path -LiteralPath $ArtDir -PathType Container)) {
    Die "art directory not found: $ArtDir"
}

# ---- scan godot/assets/art/*.png -> id -> {diffuse, normal} -----------------------------------
$entries = @{}
$pngs = Get-ChildItem -LiteralPath $ArtDir -Filter '*.png' -File -ErrorAction SilentlyContinue
foreach ($png in $pngs) {
    $base = $png.BaseName  # filename without ".png"
    if ($base -match '^(?<id>.+)_n$') {
        $id = $Matches['id']
        if (-not $entries.ContainsKey($id)) { $entries[$id] = @{ diffuse = $false; normal = $false } }
        $entries[$id].normal = $true
    }
    else {
        $id = $base
        if (-not $entries.ContainsKey($id)) { $entries[$id] = @{ diffuse = $false; normal = $false } }
        $entries[$id].diffuse = $true
    }
}

$sortedIds = @($entries.Keys)
[System.Array]::Sort($sortedIds, [System.StringComparer]::Ordinal)

# ---- deterministic JSON (hand-built -- avoids ConvertTo-Json formatting drift across the
# PowerShell 5.1 / pwsh 7 engines a dev machine might mix) --------------------------------------
function Build-ManifestJson {
    param([string[]]$Ids, [hashtable]$Entries)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('{')
    for ($i = 0; $i -lt $Ids.Length; $i++) {
        $id = $Ids[$i]
        $e = $Entries[$id]
        $diffuseStr = if ($e.diffuse) { 'true' } else { 'false' }
        $normalStr = if ($e.normal) { 'true' } else { 'false' }
        $comma = if ($i -lt $Ids.Length - 1) { ',' } else { '' }
        $lines.Add("  ""$id"": { ""diffuse"": $diffuseStr, ""normal"": $normalStr }$comma")
    }
    $lines.Add('}')
    return ($lines -join "`n") + "`n"
}

$computed = Build-ManifestJson -Ids $sortedIds -Entries $entries

if ($Check) {
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        Die "manifest not found: $ManifestPath (run 'gen-manifest.ps1' without --check to generate it)"
    }
    $onDisk = Get-Content -LiteralPath $ManifestPath -Raw
    if ($null -eq $onDisk) { $onDisk = '' }
    # Normalize line endings only -- git's `text=auto` may check this file out as CRLF on some
    # machines even though the committed blob (and this script's output) is LF.
    $onDiskNormalized = ($onDisk -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    $computedNormalized = $computed.TrimEnd("`n") + "`n"

    if ($onDiskNormalized -ceq $computedNormalized) {
        Write-Output "gen-manifest.ps1: check OK -- $($sortedIds.Length) ids match $ManifestPath"
        exit 0
    }

    [Console]::Error.WriteLine('gen-manifest.ps1: error: art-manifest.json is stale -- on-disk PNGs differ from the committed manifest.')
    [Console]::Error.WriteLine('  Rerun: pwsh art/pipeline/gen-manifest.ps1   (then commit the diff)')
    exit 1
}

# UTF-8 without BOM regardless of PowerShell version (Set-Content -Encoding utf8 on Windows
# PowerShell 5.1 would prepend a BOM; pwsh 7 would not -- write raw bytes to stay consistent).
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $computed, $utf8NoBom)
Write-Output "gen-manifest.ps1: wrote $ManifestPath ($($sortedIds.Length) ids)"
exit 0
