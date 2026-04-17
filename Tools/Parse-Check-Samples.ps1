#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Parse-check every generated script from a folder of .pblx samples.

.DESCRIPTION
    Walks the given folder for .pblx files, shells out to the PoSHBlox
    binary in --emit mode to generate the PowerShell script for each,
    then runs [Parser]::ParseInput on the output. Reports any parser
    errors per file — a catch-all for codegen bugs that produce
    syntactically invalid PowerShell.

.PARAMETER Path
    Folder to scan for .pblx files (recursive). Defaults to ./Samples
    relative to the repo root.

.PARAMETER ExePath
    Path to the PoSHBlox executable. Defaults to the most recent build
    under bin/. Accepts either a native binary or a managed DLL (the
    latter is invoked via `dotnet`).

.PARAMETER FailOnError
    Exit non-zero if any file produces parser errors. For CI use.

.EXAMPLE
    pwsh ./Tools/Parse-Check-Samples.ps1 -Path ~/MyPblxSamples
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$ExePath,
    [switch]$FailOnError
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $Path) {
    $Path = Join-Path $RepoRoot 'Samples'
}
if (-not (Test-Path $Path)) {
    Write-Error "samples folder not found: $Path"
    exit 1
}
$Path = (Resolve-Path $Path).Path

# Locate the emit executable. Prefer an explicit override; otherwise walk
# bin/ for the newest PoSHBlox.dll and invoke it via `dotnet`.
function Resolve-EmitCommand {
    param([string]$Override)

    if ($Override) {
        if (-not (Test-Path $Override)) {
            throw "ExePath not found: $Override"
        }
        $full = (Resolve-Path $Override).Path
        if ($full.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
            return @{ File = 'dotnet'; Prefix = @($full) }
        }
        return @{ File = $full; Prefix = @() }
    }

    $candidate = Get-ChildItem -Path (Join-Path $RepoRoot 'bin') -Recurse -Filter 'PoSHBlox.dll' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw "no PoSHBlox build found. Run 'dotnet build' first, or pass -ExePath."
    }
    return @{ File = 'dotnet'; Prefix = @($candidate.FullName) }
}

$emit = Resolve-EmitCommand -Override $ExePath
$samples = Get-ChildItem -Path $Path -Recurse -Filter '*.pblx' -File
if (-not $samples) {
    Write-Host "No .pblx files found under $Path"
    exit 0
}

Write-Host "Parse-checking $($samples.Count) sample(s) from $Path"
Write-Host "Using: $($emit.File) $($emit.Prefix -join ' ')"
Write-Host ''

$results = @()
foreach ($file in $samples) {
    $argList = $emit.Prefix + @('--emit', $file.FullName)
    $script = & $emit.File @argList 2>$null | Out-String
    if ($LASTEXITCODE -ne 0) {
        $results += [pscustomobject]@{
            File = $file.FullName; Status = 'EmitFailed'; Errors = @("emit exit code $LASTEXITCODE")
        }
        continue
    }

    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput(
        $script, [ref]$tokens, [ref]$parseErrors)

    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $msgs = $parseErrors | ForEach-Object {
            "  line $($_.Extent.StartLineNumber) col $($_.Extent.StartColumnNumber): $($_.Message)"
        }
        $results += [pscustomobject]@{
            File = $file.FullName; Status = 'ParseError'; Errors = $msgs
        }
    }
    else {
        $results += [pscustomobject]@{
            File = $file.FullName; Status = 'OK'; Errors = @()
        }
    }
}

$ok = @($results | Where-Object Status -EQ 'OK').Count
$bad = @($results | Where-Object Status -NE 'OK').Count

foreach ($r in $results) {
    $name = Split-Path $r.File -Leaf
    switch ($r.Status) {
        'OK'          { Write-Host "  OK         $name" -ForegroundColor Green }
        'EmitFailed'  { Write-Host "  EMIT FAIL  $name" -ForegroundColor Red
                        $r.Errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red } }
        'ParseError'  { Write-Host "  PARSE FAIL $name" -ForegroundColor Red
                        $r.Errors | ForEach-Object { Write-Host $_ -ForegroundColor Red } }
    }
}

Write-Host ''
Write-Host "Passed: $ok / $($samples.Count)   Failed: $bad"

if ($FailOnError -and $bad -gt 0) { exit 1 }
exit 0
