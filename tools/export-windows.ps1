<#
.SYNOPSIS
    Export a Windows build and zip it, so a friend can play without installing anything.

.DESCRIPTION
    This is the milestone that actually satisfies "my friends can play it." It needs no Steam
    account, no fee, and no paperwork -- those come later and are the owner's to do.

    The export is SELF-CONTAINED: Godot bundles the .NET runtime beside the exe (231 files,
    including coreclr.dll), so the machine running it needs neither .NET nor Godot installed.
    Measured 2026-08-09: 104MB exe + 62MB pck + runtime, zipping to a single archive.

    THE RISK THIS SCRIPT WAS WRITTEN TO SETTLE, recorded because the answer was not obvious:
    godot/GodotClient.csproj pins net10.0 because Godot's tooling rewrites it to net8.0 during
    import and build -- CI undoes that three times per run. Godot 4.6's officially tested C#
    target is net8.0, export runs a DIFFERENT dotnet publish path than build, and nothing
    confirmed net10.0 survived it. It does. The first real --export-release left the pin
    untouched and produced a binary that launches. If that ever stops being true, this is the
    script that will show it, so check the csproj after exporting rather than assuming.

    STYLE NOTE: ASCII-only, no here-strings, no ternary/??. Windows PowerShell 5.1 reads a
    BOM-less UTF-8 file as ANSI (mojibake) and treats an indented here-string terminator as a
    parse error. Both have bitten tools/ scripts in this repo before. Keep this file plain.

.PARAMETER GodotBin
    Path to the pinned Godot editor binary. Must match .godot-version (4.6.3-stable, .NET).

.PARAMETER OutDir
    Where the build lands. Defaults to build/windows, which .gitignore excludes -- a 160MB
    build output must never enter git history.

.PARAMETER SkipZip
    Export only, do not archive. Useful when iterating on the preset.
#>
[CmdletBinding()]
param(
    [string]$GodotBin = $env:GODOT_BIN,
    [string]$OutDir = "",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$godotProject = Join-Path $repo "godot"
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repo "build\windows"
}

if ([string]::IsNullOrWhiteSpace($GodotBin)) {
    $GodotBin = "C:\Tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe"
}
if (-not (Test-Path $GodotBin)) {
    Write-Host "export: no Godot binary at $GodotBin"
    Write-Host "        Pass -GodotBin or set GODOT_BIN. It must match .godot-version exactly;"
    Write-Host "        a different editor silently rewrites scenes and import metadata."
    exit 1
}

# Export templates are a separate ~1.2GB free download from the same Godot release, and their
# absence is the single most common reason an export fails on a fresh machine. Checking for
# them by name gives a real instruction instead of a Godot stack trace.
$templates = Join-Path $env:APPDATA "Godot\export_templates\4.6.3.stable.mono"
if (-not (Test-Path $templates)) {
    Write-Host "export: export templates missing at $templates"
    Write-Host "        Download Godot_v4.6.3-stable_mono_export_templates.tpz from the 4.6.3-stable"
    Write-Host "        release and extract its 'templates' folder to that path."
    exit 1
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}

$version = (Select-String -Path (Join-Path $godotProject "project.godot") -Pattern 'config/version="([^"]+)"').Matches[0].Groups[1].Value
Write-Host "export: Maker's Mark $version -> $OutDir"

$exe = Join-Path $OutDir "MakersMark.exe"
& $GodotBin --headless --path $godotProject --export-release "Windows Desktop" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "export: FAILED (godot exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
if (-not (Test-Path $exe)) {
    Write-Host "export: godot reported success but produced no exe at $exe"
    exit 1
}

# An export that succeeds and produces something that will not start is the failure mode that
# matters, so prove it boots rather than trusting the exit code of the exporter.
& $exe --headless --quit | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "export: the exported binary did not start cleanly (exit $LASTEXITCODE)"
    exit 1
}

$sizeMb = [math]::Round((Get-ChildItem $OutDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host "export: built and launched clean, $sizeMb MB"

if ($SkipZip) {
    exit 0
}

$zip = Join-Path (Split-Path -Parent $OutDir) ("MakersMark-" + $version + "-windows.zip")
if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $zip
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "export: $zip ($zipMb MB)"
Write-Host "        Hand this to a friend. No .NET install, no Godot, no Steam account needed."
