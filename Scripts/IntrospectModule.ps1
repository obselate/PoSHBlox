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

# Resolve module: try exact match first, then wildcard
$resolved = Get-Module -ListAvailable -Name $ModuleName -ErrorAction SilentlyContinue
if (-not $resolved) {
    $resolved = Get-Module -ListAvailable -Name "*$ModuleName*" -ErrorAction SilentlyContinue
}

if (-not $resolved) {
    Write-Error "No modules found matching '$ModuleName'."
    exit 1
}

# Deduplicate by name (same module can appear in multiple paths)
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

    $params = @()
    foreach ($p in $cmd.Parameters.GetEnumerator()) {
        $paramName = $p.Key
        if ($paramName -in $CommonParams) { continue }

        $paramInfo = $p.Value
        $paramType = "String"
        $validVals = @()
        $isMandatory = $false
        $defaultValue = ""

        # Determine type mapping
        $typeName = $paramInfo.ParameterType.Name
        switch ($typeName) {
            'SwitchParameter' { $paramType = "Bool" }
            'Int32'           { $paramType = "Int" }
            'Int64'           { $paramType = "Int" }
            'Boolean'         { $paramType = "Bool" }
            'String[]'        { $paramType = "StringArray" }
            'ScriptBlock'     { $paramType = "ScriptBlock" }
            'PSCredential'    { $paramType = "Credential" }
            default {
                if ($paramInfo.ParameterType.IsEnum) {
                    $paramType = "Enum"
                    $validVals = [System.Enum]::GetNames($paramInfo.ParameterType)
                } elseif ($typeName -match 'String') {
                    $paramType = "String"
                }
            }
        }

        # Check ValidateSet attribute
        $validateSet = $paramInfo.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
        if ($validateSet) {
            $paramType = "Enum"
            $validVals = $validateSet.ValidValues
        }

        # Check mandatory
        $mandatoryAttr = $paramInfo.Attributes | Where-Object {
            $_ -is [System.Management.Automation.ParameterAttribute] -and $_.Mandatory
        }
        if ($mandatoryAttr) { $isMandatory = $true }

        $helpMsg = ($paramInfo.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] } | Select-Object -First 1).HelpMessage
        if (-not $helpMsg) { $helpMsg = "" }

        $params += @{
            name         = $paramName
            type         = $paramType
            isMandatory  = $isMandatory
            defaultValue = $defaultValue
            description  = $helpMsg
            validValues  = @($validVals)
        }
    }

    $results += @{
        name        = $cmd.Name
        description = $synopsis
        parameters  = $params
    }
}

$results | ConvertTo-Json -Depth 5 -Compress
