$ErrorActionPreference = 'Stop'

# Discovery-only probe — no module imports. Get-Module -ListAvailable scans
# PSModulePath for module manifests and returns one entry per version. We
# collapse to latest version per module name (user rarely cares about old
# side-by-side versions for an import UX) and emit just the fields the C#
# side needs for the picker list.
#
# Output: JSON array of { name, version, description } on stdout.
# Errors: written to stderr, non-zero exit on fatal failure.

try {
    $raw = Get-Module -ListAvailable -ErrorAction SilentlyContinue
    $items = @()
    if ($raw) {
        $items = $raw |
            Group-Object -Property Name |
            ForEach-Object {
                # Latest version wins when a module is installed side-by-side.
                $latest = $_.Group | Sort-Object Version -Descending | Select-Object -First 1
                [PSCustomObject]@{
                    name        = $latest.Name
                    version     = if ($latest.Version) { $latest.Version.ToString() } else { '' }
                    description = if ($latest.Description) { $latest.Description } else { '' }
                }
            } |
            Sort-Object name
    }

    # Force array semantics so ConvertTo-Json emits [] / [obj] / [obj,...]
    # instead of $null / single object. Consistent with IntrospectModule.ps1
    # (which accumulates via $results += @{...}). Works on PS 5.1 and 7+ —
    # avoids the -AsArray switch that only exists on 7.
    $arr = @($items)
    if ($arr.Count -eq 0) {
        Write-Output '[]'
    } elseif ($arr.Count -eq 1) {
        # Single-element wrap: ConvertTo-Json unwraps a 1-element array
        # on 5.1, so manually bracket the emitted object.
        Write-Output ('[' + ($arr[0] | ConvertTo-Json -Depth 3 -Compress) + ']')
    } else {
        $arr | ConvertTo-Json -Depth 3 -Compress
    }
}
catch {
    [Console]::Error.WriteLine("ListAvailableModules failed: $($_.Exception.Message)")
    exit 1
}
