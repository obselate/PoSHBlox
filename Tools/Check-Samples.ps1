#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Integrity check for saved .pblx graphs — catches silent load-time drops.

.DESCRIPTION
    ProjectSerializer.RebuildGraph silently skips connections whose port IDs
    don't resolve, and silently drops nesting whose parent/zone can't be
    found. That's the right behavior for load robustness, but there's no
    user-visible signal when it happens — a saved graph can lose wiring or
    containment after a template rename and the user sees nothing.

    This script reads each .pblx as JSON (no .NET dependency) and reports
    every reference that *would* be silently dropped on load:

      - connection SourcePortId / TargetPortId not found on any node
      - connection SourceNodeId / TargetNodeId disagrees with the owning
        node of the referenced port
      - node.ParentNodeId references a non-existent node
      - node.ParentZoneName not present on the referenced parent's Zones
      - duplicate node IDs or duplicate port IDs
      - ActiveParameterSet not listed in KnownParameterSets

.PARAMETER Path
    Folder to scan for .pblx files (recursive). Defaults to ./Samples.

.PARAMETER FailOnError
    Exit non-zero if any file produces findings. For CI use.

.EXAMPLE
    pwsh ./Tools/Check-Samples.ps1 -Path ~/MyPblxSamples
#>
[CmdletBinding()]
param(
    [string]$Path,
    [switch]$FailOnError
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $Path) { $Path = Join-Path $RepoRoot 'Samples' }
if (-not (Test-Path $Path)) {
    Write-Error "path not found: $Path"
    exit 1
}
$Path = (Resolve-Path $Path).Path

function Test-Graph {
    param([Parameter(Mandatory)][object]$Doc, [Parameter(Mandatory)][string]$File)

    $findings = @()

    # Build lookups. A port ID → owner node ID; each ID must be unique across
    # all nodes, so collisions are themselves a finding.
    $nodesById = @{}
    $portOwner = @{}
    $dupNodes = @()
    $dupPorts = @()

    foreach ($n in @($Doc.Nodes)) {
        if ($nodesById.ContainsKey($n.Id)) { $dupNodes += $n.Id }
        else { $nodesById[$n.Id] = $n }

        foreach ($p in (@($n.Inputs) + @($n.Outputs))) {
            if (-not $p) { continue }
            if ($portOwner.ContainsKey($p.Id)) { $dupPorts += $p.Id }
            else { $portOwner[$p.Id] = $n.Id }
        }
    }

    foreach ($id in ($dupNodes | Select-Object -Unique)) {
        $findings += "duplicate node id: $id"
    }
    foreach ($id in ($dupPorts | Select-Object -Unique)) {
        $findings += "duplicate port id: $id"
    }

    # Connections: both endpoints must resolve, and the declared owner node
    # must match the port's actual owner.
    foreach ($c in @($Doc.Connections)) {
        $srcOwner = $portOwner[$c.SourcePortId]
        $tgtOwner = $portOwner[$c.TargetPortId]

        if (-not $srcOwner) {
            $findings += "dangling connection source port: $($c.SourcePortId) (→ $($c.TargetPortId))"
        }
        elseif ($c.SourceNodeId -and $srcOwner -ne $c.SourceNodeId) {
            $findings += "connection source node/port mismatch: port $($c.SourcePortId) belongs to $srcOwner but wire says $($c.SourceNodeId)"
        }

        if (-not $tgtOwner) {
            $findings += "dangling connection target port: $($c.TargetPortId) (← $($c.SourcePortId))"
        }
        elseif ($c.TargetNodeId -and $tgtOwner -ne $c.TargetNodeId) {
            $findings += "connection target node/port mismatch: port $($c.TargetPortId) belongs to $tgtOwner but wire says $($c.TargetNodeId)"
        }
    }

    # Nesting: ParentNodeId must exist, and ParentZoneName must be one of
    # the parent's declared zones.
    foreach ($n in @($Doc.Nodes)) {
        if (-not $n.ParentNodeId) { continue }

        $parent = $nodesById[$n.ParentNodeId]
        if (-not $parent) {
            $findings += "node $($n.Id) ($($n.Title)): parent $($n.ParentNodeId) not found"
            continue
        }

        if ($n.ParentZoneName) {
            $zones = @($parent.Zones | ForEach-Object { $_.Name })
            if ($zones -notcontains $n.ParentZoneName) {
                $findings += "node $($n.Id) ($($n.Title)): zone '$($n.ParentZoneName)' not on parent $($parent.Id)"
            }
        }
    }

    # Parameter-set consistency.
    foreach ($n in @($Doc.Nodes)) {
        if (-not $n.ActiveParameterSet) { continue }
        $sets = @($n.KnownParameterSets)
        if ($sets.Count -gt 0 -and $sets -notcontains $n.ActiveParameterSet) {
            $findings += "node $($n.Id) ($($n.Title)): ActiveParameterSet '$($n.ActiveParameterSet)' not in KnownParameterSets"
        }
    }

    return $findings
}

$samples = Get-ChildItem -Path $Path -Recurse -Filter '*.pblx' -File
if (-not $samples) {
    Write-Host "No .pblx files found under $Path"
    exit 0
}

Write-Host "Checking $($samples.Count) sample(s) from $Path"
Write-Host ''

$totalFindings = 0
foreach ($file in $samples) {
    try {
        $doc = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    }
    catch {
        Write-Host "  READ FAIL $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
        $totalFindings++
        continue
    }

    $findings = Test-Graph -Doc $doc -File $file.FullName
    if ($findings.Count -eq 0) {
        Write-Host "  OK      $($file.Name)" -ForegroundColor Green
    }
    else {
        Write-Host "  ISSUES  $($file.Name)" -ForegroundColor Yellow
        foreach ($f in $findings) {
            Write-Host "    $f" -ForegroundColor Yellow
        }
        $totalFindings += $findings.Count
    }
}

Write-Host ''
Write-Host "Files scanned: $($samples.Count)   Findings: $totalFindings"

if ($FailOnError -and $totalFindings -gt 0) { exit 1 }
exit 0
