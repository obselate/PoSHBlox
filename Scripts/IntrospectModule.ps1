param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName
)

$ErrorActionPreference = 'Stop'

# Common parameters to skip
$CommonParams = @(
    'Verbose','Debug','ErrorAction','WarningAction','InformationAction',
    'ErrorVariable','WarningVariable','InformationVariable','OutVariable',
    'OutBuffer','PipelineVariable','Confirm','WhatIf'
)

function Map-ParamType {
    param([System.Reflection.ParameterInfo]$pi, [System.Management.Automation.ParameterMetadata]$pm)
    $t = $pm.ParameterType
    $name = $t.Name

    if ($pm.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }) {
        return 'Enum'
    }

    switch -Regex ($name) {
        '^SwitchParameter$'                             { return 'Bool' }
        '^Boolean$'                                     { return 'Bool' }
        '^Int(16|32|64)?$'                              { return 'Int' }
        '^UInt(16|32|64)?$'                             { return 'Int' }
        '^Byte$'                                        { return 'Int' }
        '^Double$|^Single$|^Decimal$'                   { return 'Int' } # numeric bucket
        '^String\[\]$'                                  { return 'StringArray' }
        '^Object\[\]$'                                  { return 'Collection' }
        '^.*\[\]$'                                      { return 'Collection' }
        '^ScriptBlock$'                                 { return 'ScriptBlock' }
        '^PSCredential$'                                { return 'Credential' }
        '^Hashtable$'                                   { return 'HashTable' }
        '^String$'                                      {
            # Path-ish heuristic -- if the param name hints path, type as Path
            if ($pm.Name -match '^(Path|LiteralPath|Destination|FilePath|OutputPath|WorkingDirectory|LogPath|SourcePath)$') {
                return 'Path'
            }
            return 'String'
        }
        default {
            if ($t.IsEnum) { return 'Enum' }
            return 'Object'
        }
    }
}

# Resolve module. Three paths in priority order:
#   1. Get-Module -ListAvailable -- normal modules on disk.
#   2. Wildcard match against the same.
#   3. Get-Command -Module -- catches built-in snap-ins (Microsoft.PowerShell.Core
#      et al in Windows PowerShell 5.1) that Get-Module can't see but whose
#      cmdlets are always importable.
$resolved = Get-Module -ListAvailable -Name $ModuleName -ErrorAction SilentlyContinue
if (-not $resolved) {
    $resolved = Get-Module -ListAvailable -Name "*$ModuleName*" -ErrorAction SilentlyContinue
}

$actualName = $null
if ($resolved) {
    $uniqueNames = $resolved | Select-Object -ExpandProperty Name -Unique
    if ($uniqueNames.Count -gt 1) {
        $list = ($uniqueNames | Sort-Object) -join ', '
        Write-Error "Multiple modules match '$ModuleName': $list  -- please be more specific."
        exit 1
    }
    $actualName = $uniqueNames | Select-Object -First 1
} else {
    # Snap-in / always-loaded module fallback. If any command is published
    # under this module name, take the name as given and rely on cmdlet
    # availability rather than module import.
    $existing = Get-Command -Module $ModuleName -CommandType Cmdlet,Function -ErrorAction SilentlyContinue
    if ($existing) {
        $actualName = $ModuleName
    } else {
        Write-Error "No modules found matching '$ModuleName'."
        exit 1
    }
}

# Import if not already loaded. Swallow non-fatal errors/warnings -- some
# modules (notably Microsoft.PowerShell.Security on 5.1) raise TypeData
# re-registration warnings that get promoted to terminating errors under
# -ErrorAction Stop even though the module itself loads fine.
if (-not (Get-Module -Name $actualName)) {
    try {
        Import-Module $actualName -ErrorAction SilentlyContinue -WarningAction SilentlyContinue *>$null
    } catch { }
}

# Declared exports from the manifest -- used both for the auto-load probe
# below and as a last-resort enumeration source if Get-Command -Module keeps
# returning empty.
$manifest = Get-Module -ListAvailable -Name $actualName -ErrorAction SilentlyContinue | Select-Object -First 1
$declaredExports = @()
if ($manifest -and $manifest.ExportedCommands -and $manifest.ExportedCommands.Count -gt 0) {
    $declaredExports = @($manifest.ExportedCommands.Keys)
}
if ($declaredExports.Count -eq 0 -and $manifest -and $manifest.Path -and (Test-Path $manifest.Path)) {
    try {
        $data = Import-PowerShellDataFile -Path $manifest.Path -ErrorAction Stop
        if ($data.CmdletsToExport)   { $declaredExports += @($data.CmdletsToExport) }
        if ($data.FunctionsToExport) { $declaredExports += @($data.FunctionsToExport) }
        $declaredExports = $declaredExports | Where-Object { $_ -and $_ -notmatch '^[\*\?]+$' }
    } catch { }
}

# If Get-Command -Module is still empty, hit one declared export with
# Get-Command <name> -- PS's on-demand auto-loader is more tolerant of
# type conflicts than explicit Import-Module.
if (-not (Get-Module -Name $actualName) -and
    -not (Get-Command -Module $actualName -ErrorAction SilentlyContinue))
{
    $probe = $declaredExports | Select-Object -First 1
    if ($probe) {
        Get-Command $probe -ErrorAction SilentlyContinue *>$null
    }
}

# Collect the commands we will introspect. Preferred: Get-Command -Module,
# which picks up everything that belongs to the module including dynamically
# added functions. Fallback: iterate declared exports individually -- each
# Get-Command <name> call triggers auto-load if the command isn't reachable
# yet, and returns CommandInfo objects we can pass to the enumeration loop.
$commands = @(Get-Command -Module $actualName -CommandType Cmdlet,Function -ErrorAction SilentlyContinue | Sort-Object Name)
if ($commands.Count -eq 0 -and $declaredExports.Count -gt 0) {
    $collected = @()
    foreach ($name in $declaredExports) {
        $c = Get-Command $name -ErrorAction SilentlyContinue
        if ($c) { $collected += $c }
    }
    $commands = @($collected | Sort-Object Name)
}

if ($commands.Count -eq 0) {
    $gmCount = (Get-Module -Name $actualName | Measure-Object).Count
    $gcmCount = (Get-Command -Module $actualName -ErrorAction SilentlyContinue | Measure-Object).Count
    $manifestState = if ($manifest) { 'yes' } else { 'no' }
    $diag = "Get-Module=$gmCount, Get-Command -Module=$gcmCount, declaredExports=$($declaredExports.Count), manifest=$manifestState"
    Write-Error "Failed to introspect module '$actualName' - no commands reachable. [$diag]"
    exit 1
}

# Write resolved name to stderr so C# can read it (not mixed into JSON stdout)
[Console]::Error.WriteLine("RESOLVED:$actualName")

# $commands was resolved above during the import / probe / fallback sequence.

$results = @()

foreach ($cmd in $commands) {
    $synopsis = ""
    try {
        $help = Get-Help $cmd.Name -ErrorAction SilentlyContinue
        if ($help.Synopsis) {
            $synopsis = ($help.Synopsis -replace '\r?\n', ' ').Trim()
            if ($synopsis.Length -gt 120) {
                $synopsis = $synopsis.Substring(0, 117) + "..."
            }
        }
    } catch { }

    # Parameter sets the cmdlet declares. CmdletBindingAttribute.DefaultParameterSetName
    # is the preferred default. ParameterSets collection provides the list and per-set flags.
    $knownSets = @()
    $defaultSet = $null
    try {
        $defaultSet = $cmd.DefaultParameterSet
        if ($cmd.ParameterSets) {
            foreach ($ps in $cmd.ParameterSets) {
                if ($ps.Name -and $ps.Name -ne '__AllParameterSets') {
                    $knownSets += $ps.Name
                }
            }
        }
    } catch { }

    $params = @()
    $primaryPipelineParam = $null
    foreach ($p in $cmd.Parameters.GetEnumerator()) {
        $paramName = $p.Key
        if ($paramName -in $CommonParams) { continue }

        $paramInfo = $p.Value
        $paramType = Map-ParamType -pi $null -pm $paramInfo
        $validVals = @()
        $isMandatory = $false
        $defaultValue = ""
        $isPipelineInput = $false
        $paramSets = @()
        $mandatoryInSets = @()

        # ValidateSet overrides the mapped type to Enum.
        $validateSet = $paramInfo.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        if ($validateSet) {
            $paramType = "Enum"
            $validVals = $validateSet.ValidValues
        } elseif ($paramInfo.ParameterType.IsEnum) {
            $validVals = [System.Enum]::GetNames($paramInfo.ParameterType)
        }

        # Parameter attributes: Mandatory + ValueFromPipeline + ParameterSetName.
        # Params can appear in multiple [Parameter(...)] attributes, one per set.
        $paramAttrs = $paramInfo.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] }
        foreach ($attr in $paramAttrs) {
            if ($attr.Mandatory) { $isMandatory = $true }
            if ($attr.ValueFromPipeline) { $isPipelineInput = $true }

            # __AllParameterSets is PS's sentinel for "applies to every set" --
            # we represent that as an empty sets list (our model's same semantics).
            $setName = $attr.ParameterSetName
            if ($setName -and $setName -ne '__AllParameterSets') {
                if ($paramSets -notcontains $setName) { $paramSets += $setName }
                if ($attr.Mandatory -and $mandatoryInSets -notcontains $setName) {
                    $mandatoryInSets += $setName
                }
            }
        }

        $helpMsg = ($paramAttrs | Select-Object -First 1).HelpMessage
        if (-not $helpMsg) { $helpMsg = "" }

        if ($isPipelineInput -and -not $primaryPipelineParam) {
            $primaryPipelineParam = $paramName
        }

        $params += @{
            name            = $paramName
            type            = $paramType
            isMandatory     = $isMandatory
            defaultValue    = $defaultValue
            description     = $helpMsg
            validValues     = @($validVals)
            isPipelineInput = $isPipelineInput
            parameterSets   = @($paramSets)
            mandatoryInSets = @($mandatoryInSets)
        }
    }

    # Output types -> V2 DataOutputs. If the cmdlet declares OutputType, name
    # the primary after the last segment of the first declared type; otherwise
    # fall back to a single primary "Out" of type Any.
    $dataOutputs = @()
    $outputTypes = @()
    try {
        $outputTypes = $cmd.OutputType
    } catch { }

    if ($outputTypes -and $outputTypes.Count -gt 0) {
        $first = $outputTypes | Select-Object -First 1
        $typeName = if ($first.Type) { $first.Type.Name } else { "$($first.Name)" }
        $shortName = ($typeName -split '\.')[-1]
        if (-not $shortName) { $shortName = "Out" }
        $dataOutputs += @{
            name      = $shortName
            type      = "Any"
            isPrimary = $true
        }
    } else {
        $dataOutputs += @{
            name      = "Out"
            type      = "Any"
            isPrimary = $true
        }
    }

    $results += @{
        name                      = $cmd.Name
        description               = $synopsis
        parameters                = $params
        hasExecIn                 = $true
        hasExecOut                = $true
        primaryPipelineParameter  = $primaryPipelineParam
        dataOutputs               = $dataOutputs
        knownParameterSets        = @($knownSets)
        defaultParameterSet       = $defaultSet
    }
}

$results | ConvertTo-Json -Depth 6 -Compress
