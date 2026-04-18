#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Schema-level auditor for PoSHBlox built-in template catalogs.

.DESCRIPTION
    Walks every JSON catalog under Templates/Builtin and reports schema
    violations: missing/duplicate fields, dangling param-set references,
    primary-pipeline pointers that don't resolve, type-default mismatches
    (e.g. Bool default of "yes"), multiple primary outputs, etc. Pure
    schema check — does not introspect against a live PowerShell host.

.PARAMETER Path
    One or more catalog roots to audit. Accepts an array. Defaults to
    ../Templates/Builtin plus ../Templates/Custom (source-tree layout);
    for runtime-imported catalogs, point this at
    <app>/bin/<config>/net10.0/Templates/Custom where SaveCustomCatalogAsync
    writes new user imports.

.PARAMETER FailOnError
    Exit non-zero if any Error-severity finding is reported. For CI use.

.EXAMPLE
    pwsh ./Tools/Audit-Templates.ps1
    pwsh ./Tools/Audit-Templates.ps1 -Path bin/Debug/net10.0/Templates/Custom
#>
[CmdletBinding()]
param(
    [string[]]$Path,
    [switch]$FailOnError
)

$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path $PSScriptRoot '..'

if (-not $Path -or $Path.Count -eq 0) {
    # Default search list: source catalogs plus any runtime Custom folder
    # the app has written under bin/ (Debug or Release). The runtime path
    # is where SaveCustomCatalogAsync writes newly-imported modules.
    $defaults = @(
        (Join-Path $repoRoot 'Templates' 'Builtin'),
        (Join-Path $repoRoot 'Templates' 'Custom')
    )
    foreach ($cfg in 'Debug', 'Release') {
        $runtime = Join-Path $repoRoot "bin/$cfg/net10.0/Templates/Custom"
        if (Test-Path $runtime) { $defaults += $runtime }
    }
    $Path = $defaults | Where-Object { Test-Path $_ }
    if (-not $Path) {
        Write-Error "No default catalog folders found. Pass -Path explicitly."
        exit 1
    }
}

# Filter user-supplied paths: warn on missing, skip instead of hard-erroring.
$resolved = @()
foreach ($p in $Path) {
    if (-not (Test-Path $p)) {
        Write-Warning "skipping (not found): $p"
        continue
    }
    $resolved += (Resolve-Path $p).Path
}
$Path = $resolved
if (-not $Path) {
    Write-Error "No catalog folders resolved. Check the -Path argument."
    exit 1
}

$ValidParamTypes = @(
    'Any','String','Int','Bool','Path','StringArray','Object',
    'Collection','ScriptBlock','Credential','HashTable','Enum'
)
$ValidContainerTypes = @('None','IfElse','ForEach','TryCatch','While','Function','Label')
$ValidNodeKinds = @('Cmdlet','Value')
$ValidEditions = @('pwsh','powershell')

$findings = [System.Collections.ArrayList]::new()

function Add-Finding {
    param(
        [Parameter(Mandatory)][ValidateSet('Error','Warning','Info')] $Severity,
        [Parameter(Mandatory)] $File,
        [Parameter(Mandatory)] $Template,
        [Parameter(Mandatory)] $Message,
        $Param
    )
    [void]$findings.Add([pscustomobject]@{
        Severity = $Severity
        File     = $File
        Template = $Template
        Param    = $Param
        Message  = $Message
    })
}

function Test-Template {
    param($cat, $tpl, $file)

    $name = if ($tpl.name) { $tpl.name } else { '<unnamed>' }

    # Container type defaulting — empty string means None.
    $containerType = if ($tpl.containerType) { $tpl.containerType } else { 'None' }
    $kind          = if ($tpl.kind)          { $tpl.kind }          else { 'Cmdlet' }

    if (-not $tpl.name) {
        Add-Finding Error $file $name 'Template has no name.'
    }

    if ($kind -notin $ValidNodeKinds) {
        Add-Finding Error $file $name "kind '$kind' is not one of $($ValidNodeKinds -join ', ')."
    }
    if ($containerType -notin $ValidContainerTypes) {
        Add-Finding Error $file $name "containerType '$containerType' is not valid."
    }

    # Orphan: nothing to emit at codegen.
    $hasCmdlet     = -not [string]::IsNullOrWhiteSpace($tpl.cmdletName)
    $hasScriptBody = -not [string]::IsNullOrWhiteSpace($tpl.scriptBody)
    $hasValueExpr  = -not [string]::IsNullOrWhiteSpace($tpl.valueExpression)
    $isContainer   = $containerType -ne 'None'

    if ($kind -eq 'Value') {
        if (-not $hasValueExpr) {
            Add-Finding Error $file $name 'Value-kind template missing valueExpression.'
        }
        if ($hasCmdlet -or $hasScriptBody) {
            Add-Finding Warning $file $name 'Value-kind template should not set cmdletName or scriptBody.'
        }
    } else {
        if (-not $hasCmdlet -and -not $hasScriptBody -and -not $isContainer) {
            Add-Finding Error $file $name 'Cmdlet-kind template has no cmdletName, scriptBody, or container shape — would emit nothing.'
        }
    }

    # DataOutputs: at most one primary, types valid, names non-empty.
    $primaries = @($tpl.dataOutputs | Where-Object { $_.isPrimary })
    if ($primaries.Count -gt 1) {
        $names = ($primaries | ForEach-Object { $_.name }) -join ', '
        Add-Finding Error $file $name "Multiple data outputs flagged isPrimary: $names"
    }
    foreach ($out in $tpl.dataOutputs) {
        $outType = if ($out.type) { $out.type } else { 'Any' }
        if ([string]::IsNullOrWhiteSpace($out.name)) {
            Add-Finding Error $file $name 'A dataOutput has empty name.'
        }
        if ($outType -notin $ValidParamTypes) {
            Add-Finding Error $file $name "dataOutput '$($out.name)' has invalid type '$outType'."
        }
    }

    # Parameter-set sanity.
    $declaredSets = @($tpl.knownParameterSets)
    if ($tpl.defaultParameterSet -and $declaredSets.Count -gt 0 -and
        $tpl.defaultParameterSet -notin $declaredSets) {
        Add-Finding Error $file $name "defaultParameterSet '$($tpl.defaultParameterSet)' not in knownParameterSets."
    }

    # SupportedEditions: must be valid edition tokens when present.
    foreach ($ed in @($tpl.supportedEditions)) {
        if ($ed -and $ed -notin $ValidEditions) {
            Add-Finding Warning $file $name "supportedEditions contains unknown edition '$ed'."
        }
    }

    # Parameters: per-param checks + primary-pipeline pointer resolution.
    $paramNames = @{}
    $pipelineFlagged = @()
    foreach ($p in $tpl.parameters) {
        $pName = if ($p.name) { $p.name } else { '<unnamed>' }

        if ([string]::IsNullOrWhiteSpace($p.name)) {
            Add-Finding Error $file $name 'A parameter has empty name.' $pName
            continue
        }

        if ($paramNames.ContainsKey($p.name)) {
            Add-Finding Error $file $name "Duplicate parameter name '$($p.name)'." $pName
        }
        $paramNames[$p.name] = $true

        $pType = if ($p.type) { $p.type } else { 'String' }
        if ($pType -notin $ValidParamTypes) {
            Add-Finding Error $file $name "Parameter '$($p.name)' has invalid type '$pType'." $pName
        }

        # Bool defaults must look like a bool.
        if ($pType -eq 'Bool' -and $p.PSObject.Properties['defaultValue'] -and
            -not [string]::IsNullOrEmpty($p.defaultValue) -and
            $p.defaultValue -notin @('true','false','$true','$false','True','False')) {
            Add-Finding Warning $file $name "Bool parameter '$($p.name)' has non-bool defaultValue '$($p.defaultValue)'." $pName
        }

        # ValidValues only meaningful for String/Enum-shaped params.
        # @($null).Count is 1, not 0 — filter nulls out so missing JSON
        # properties don't read as a single-element array.
        $validCount = @($p.validValues | Where-Object { $_ }).Count
        if ($validCount -gt 0 -and $pType -notin @('String','Enum','Path')) {
            Add-Finding Warning $file $name "Parameter '$($p.name)' has validValues but type '$pType' isn't enum-like." $pName
        }

        # ParameterSets / MandatoryInSets must reference declared sets.
        foreach ($set in @($p.parameterSets)) {
            if ($set -and $declaredSets.Count -gt 0 -and $set -notin $declaredSets) {
                Add-Finding Error $file $name "Parameter '$($p.name)' references undeclared set '$set'." $pName
            }
        }
        foreach ($set in @($p.mandatoryInSets)) {
            if ($set -and $declaredSets.Count -gt 0 -and $set -notin $declaredSets) {
                Add-Finding Error $file $name "Parameter '$($p.name)' mandatoryInSets references undeclared '$set'." $pName
            }
        }

        if ($p.isPipelineInput) {
            $pipelineFlagged += $p.name
        }
    }

    # primaryPipelineParameter must resolve.
    if ($tpl.primaryPipelineParameter -and -not $paramNames.ContainsKey($tpl.primaryPipelineParameter)) {
        Add-Finding Error $file $name "primaryPipelineParameter '$($tpl.primaryPipelineParameter)' has no matching parameter."
    }

    # If the template declares a primary pipeline param, it should also be
    # one of the IsPipelineInput-flagged params (otherwise codegen marks an
    # un-pipelineable pin as the primary target).
    if ($tpl.primaryPipelineParameter -and $pipelineFlagged.Count -gt 0 -and
        $tpl.primaryPipelineParameter -notin $pipelineFlagged) {
        Add-Finding Warning $file $name "primaryPipelineParameter '$($tpl.primaryPipelineParameter)' isn't flagged isPipelineInput."
    }

    # Multiple isPipelineInput params with no explicit primary → ambiguous.
    if ($pipelineFlagged.Count -gt 1 -and -not $tpl.primaryPipelineParameter) {
        $list = $pipelineFlagged -join ', '
        Add-Finding Warning $file $name "Multiple isPipelineInput params ($list) but no primaryPipelineParameter set — codegen will pick the first."
    }
}

# ── Walk catalogs ─────────────────────────────────────────────
$catalogs = @()
foreach ($root in $Path) {
    $catalogs += Get-ChildItem -Path $root -Filter '*.json' -File
}
Write-Host "Auditing $($catalogs.Count) catalog file(s) across:" -ForegroundColor Cyan
foreach ($root in $Path) { Write-Host "  $root" -ForegroundColor Cyan }

foreach ($file in $catalogs) {
    # Include the parent folder so findings from Builtin vs Custom are
    # distinguishable in the output table.
    $label = "$(Split-Path $file.Directory.FullName -Leaf)/$($file.Name)"

    try {
        $cat = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    } catch {
        Add-Finding Error $label '<catalog>' "Failed to parse JSON: $($_.Exception.Message)"
        continue
    }

    if (-not $cat.templates) {
        Add-Finding Warning $label '<catalog>' 'Catalog has no templates array.'
        continue
    }

    foreach ($tpl in $cat.templates) {
        Test-Template -cat $cat -tpl $tpl -file $label
    }
}

# ── Report ────────────────────────────────────────────────────
$bySeverity   = $findings | Group-Object Severity
$errorMatch   = @($bySeverity | Where-Object Name -eq 'Error')
$warningMatch = @($bySeverity | Where-Object Name -eq 'Warning')
$infoMatch    = @($bySeverity | Where-Object Name -eq 'Info')
$errors   = if ($errorMatch.Count   -gt 0) { $errorMatch[0].Count }   else { 0 }
$warnings = if ($warningMatch.Count -gt 0) { $warningMatch[0].Count } else { 0 }
$infos    = if ($infoMatch.Count    -gt 0) { $infoMatch[0].Count }    else { 0 }

Write-Host ""
Write-Host "── Summary ───────────────────────────────" -ForegroundColor Cyan
$errColor  = if ($errors)   { 'Red' }    else { 'Green' }
$warnColor = if ($warnings) { 'Yellow' } else { 'Green' }
Write-Host "Errors:   $errors"   -ForegroundColor $errColor
Write-Host "Warnings: $warnings" -ForegroundColor $warnColor
if ($infos) { Write-Host "Info:     $infos" -ForegroundColor Gray }
Write-Host ""

if ($findings.Count -gt 0) {
    $findings |
        Sort-Object @{Expression={
            switch ($_.Severity) { 'Error' { 0 } 'Warning' { 1 } default { 2 } }
        }}, File, Template |
        Format-Table -AutoSize -Wrap Severity, File, Template, Param, Message
}

if ($FailOnError -and $errors -gt 0) {
    exit 1
}
