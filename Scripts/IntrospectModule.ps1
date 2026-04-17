param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName
)

$ErrorActionPreference = 'Stop'

# Force module auto-loading on. Inherited env settings can set this to None
# or ModuleQualified, defeating the Get-Command probe path we rely on for
# modules whose Import-Module fails on type conflicts.
$PSModuleAutoloadingPreference = 'All'

# Common parameters to skip. Includes PS 7.2+ additions (ProgressAction).
$CommonParams = @(
    'Verbose','Debug','ErrorAction','WarningAction','InformationAction','ProgressAction',
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

# Declared exports from the manifest -- used as a last-resort enumeration
# source if Get-Command -Module returns empty, and for the auto-load probe.
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

# Strategy: try PowerShell's on-demand auto-loader FIRST via Get-Command.
# Auto-load tolerates type conflicts that Import-Module elevates to errors
# (Microsoft.PowerShell.Security on 5.1 is the canonical offender). Doing
# Import-Module first poisons the auto-loader's "already tried" state for
# that module, blocking subsequent auto-load attempts.
if (-not (Get-Module -Name $actualName) -and $declaredExports.Count -gt 0) {
    $probe = $declaredExports | Select-Object -First 1
    Get-Command $probe -ErrorAction SilentlyContinue *>$null
}

# If auto-load didn't pull the module in, fall back to explicit Import-Module
# with all streams suppressed. Most modules that fail auto-load succeed here
# (custom user modules, modules that aren't in the auto-load path).
$importErrors = @()
if (-not (Get-Module -Name $actualName) -and
    -not (Get-Command -Module $actualName -ErrorAction SilentlyContinue))
{
    try {
        Import-Module $actualName -ErrorAction SilentlyContinue -WarningAction SilentlyContinue -ErrorVariable +importErrors *>$null
    } catch { $importErrors += $_.ToString() }
}

# Last-resort import: -Force -Global bypasses stuck "already tried" state
# that PS 5.1 can enter for compiled modules (Microsoft.PowerShell.Security
# in particular). Safe because the host process is single-use and exits
# after this script runs.
if (-not (Get-Module -Name $actualName) -and
    -not (Get-Command -Module $actualName -ErrorAction SilentlyContinue))
{
    try {
        Import-Module $actualName -Force -Global -DisableNameChecking `
            -ErrorAction SilentlyContinue -WarningAction SilentlyContinue -ErrorVariable +importErrors *>$null
    } catch { $importErrors += $_.ToString() }
}

# Collect the commands we will introspect. Preferred: Get-Command -Module,
# which picks up everything that belongs to the module including dynamically
# added functions. Fallback: iterate declared exports individually -- each
# Get-Command <name> call triggers auto-load if the command isn't reachable
# yet, and returns CommandInfo objects we can pass to the enumeration loop.
$commands = @(Get-Command -Module $actualName -CommandType Cmdlet,Function -ErrorAction SilentlyContinue | Sort-Object Name)
if ($commands.Count -eq 0 -and $declaredExports.Count -gt 0) {
    # PS 5.1 compiled-module quirk: Microsoft.PowerShell.Security can load
    # but not respond to -Module filtering. Resolve each declared export by
    # plain name first, then by module-qualified name as a last resort.
    $collected = @()
    foreach ($name in $declaredExports) {
        $c = Get-Command $name -ErrorAction SilentlyContinue |
             Where-Object { $_.CommandType -in 'Cmdlet','Function' } |
             Select-Object -First 1
        if (-not $c) {
            $c = Get-Command "$actualName\$name" -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        }
        if ($c) { $collected += $c }
    }
    $commands = @($collected | Sort-Object Name -Unique)
}

# Absolute last-resort: enumerate every command in the session and match on
# the ModuleName / Source property. Slower than -Module filtering but works
# when the module filter is broken (seen on some Windows 11 / PS 5.1 builds
# for Microsoft.PowerShell.Security).
if ($commands.Count -eq 0) {
    $commands = @(Get-Command -CommandType Cmdlet,Function -ErrorAction SilentlyContinue |
                  Where-Object { $_.ModuleName -eq $actualName -or $_.Source -eq $actualName } |
                  Sort-Object Name -Unique)
}

if ($commands.Count -eq 0) {
    $gmCount = (Get-Module -Name $actualName | Measure-Object).Count
    $gcmCount = (Get-Command -Module $actualName -ErrorAction SilentlyContinue | Measure-Object).Count
    $manifestState = if ($manifest) { 'yes' } else { 'no' }
    $diag = "Get-Module=$gmCount, Get-Command -Module=$gcmCount, declaredExports=$($declaredExports.Count), manifest=$manifestState"
    if ($importErrors.Count -gt 0) {
        $errSummary = ($importErrors | ForEach-Object { $_.ToString() } | Select-Object -First 3) -join ' || '
        [Console]::Error.WriteLine("[introspect]   import-errors: $errSummary")
    }
    Write-Error "Failed to introspect module '$actualName' - no commands reachable. [$diag]"
    exit 1
}

# Write resolved name + host provenance to stderr so C# can read them (not
# mixed into JSON stdout). HOST helps standalone runs identify their version
# without re-invoking PS and cross-checks what C# thinks it launched.
$edition = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
[Console]::Error.WriteLine("RESOLVED:$actualName")
[Console]::Error.WriteLine("HOST:$edition-$($PSVersionTable.PSVersion)")

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

        # Description precedence:
        #   1. [Parameter(HelpMessage='...')] attribute    -- author-set, concise.
        #   2. Get-Help's per-parameter 'description' text -- MS-maintained doc
        #      prose, often a full paragraph.
        #   3. empty string                                -- don't block the UI.
        # We store the full text (newlines collapsed, whitespace normalized);
        # the properties panel truncates to a couple of lines for display and
        # surfaces the rest on hover. No length cap here.
        $helpMsg = ($paramAttrs | Select-Object -First 1).HelpMessage
        if (-not $helpMsg -and $help -and $help.parameters -and $help.parameters.parameter) {
            $ph = $help.parameters.parameter | Where-Object { $_.name -eq $paramName } | Select-Object -First 1
            if ($ph -and $ph.description) {
                $text = ($ph.description | ForEach-Object { $_.Text }) -join ' '
                $text = ($text -replace '\r?\n', ' ' -replace '\s{2,}', ' ').Trim()
                $helpMsg = $text
            }
        }
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
