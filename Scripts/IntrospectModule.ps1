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
            # Path-ish heuristic — if the param name hints path, type as Path
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

# Resolve module: try exact match first, then wildcard
$resolved = Get-Module -ListAvailable -Name $ModuleName -ErrorAction SilentlyContinue
if (-not $resolved) {
    $resolved = Get-Module -ListAvailable -Name "*$ModuleName*" -ErrorAction SilentlyContinue
}

if (-not $resolved) {
    Write-Error "No modules found matching '$ModuleName'."
    exit 1
}

$uniqueNames = $resolved | Select-Object -ExpandProperty Name -Unique

if ($uniqueNames.Count -gt 1) {
    $list = ($uniqueNames | Sort-Object) -join ', '
    Write-Error "Multiple modules match '$ModuleName': $list  -- please be more specific."
    exit 1
}

$actualName = $uniqueNames | Select-Object -First 1

try {
    Import-Module $actualName -ErrorAction Stop
} catch {
    Write-Error "Failed to import module '$actualName': $_"
    exit 1
}

# Write resolved name to stderr so C# can read it (not mixed into JSON stdout)
[Console]::Error.WriteLine("RESOLVED:$actualName")

$commands = Get-Command -Module $actualName -CommandType Cmdlet,Function | Sort-Object Name

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

            # __AllParameterSets is PS's sentinel for "applies to every set" —
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

    # Output types → V2 DataOutputs. If the cmdlet declares OutputType, name
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
