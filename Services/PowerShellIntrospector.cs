using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PoSHBlox.Services;

/// <summary>
/// Launches PowerShell to introspect a module's cmdlets and parameters.
/// Host selection is delegated to <see cref="PowerShellHostRegistry"/> so Run
/// and Introspect share one resolver — pwsh 7+ when present, 5.1 as fallback.
/// The resolved host id is stamped into <see cref="IntrospectionResult.HostId"/>
/// so catalog writers can record which host produced the metadata.
/// </summary>
public static class PowerShellIntrospector
{
    private static readonly JsonSerializerOptions Options = new(PblxJsonContext.Default.Options);

    /// <summary>
    /// Introspect <paramref name="moduleName"/>. When <paramref name="host"/>
    /// is null the registry's default (pwsh-preferred) is used.
    /// </summary>
    public static async Task<IntrospectionResult> IntrospectModuleAsync(string moduleName, PowerShellHost? host = null)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "IntrospectModule.ps1");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Introspection script not found.", scriptPath);

        host ??= PowerShellHostRegistry.Default
            ?? throw new InvalidOperationException("No PowerShell host detected on PATH (pwsh.exe or powershell.exe).");

        Console.Out.WriteLine($"[regen]   using host '{host.DisplayName}' ({host.Executable})");

        var psi = new ProcessStartInfo
        {
            FileName = host.Executable,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ModuleName \"{moduleName}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PowerShell process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Trim());

        // Parse resolved module name from stderr (RESOLVED:ActualName)
        string resolvedName = moduleName;
        var resolvedLine = stderr
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("RESOLVED:", StringComparison.Ordinal));

        if (resolvedLine != null)
            resolvedName = resolvedLine.Substring("RESOLVED:".Length).Trim();

        if (string.IsNullOrWhiteSpace(stdout))
            return new IntrospectionResult { ResolvedModuleName = resolvedName, HostId = host.Id, Cmdlets = [] };

        var cmdlets = JsonSerializer.Deserialize<List<DiscoveredCmdlet>>(stdout, Options) ?? [];
        return new IntrospectionResult { ResolvedModuleName = resolvedName, HostId = host.Id, Cmdlets = cmdlets };
    }
}

public class IntrospectionResult
{
    public string ResolvedModuleName { get; set; } = "";

    /// <summary>Id of the host that produced this result (e.g. <c>"pwsh-7.4.1"</c>).</summary>
    public string HostId { get; set; } = "";

    public List<DiscoveredCmdlet> Cmdlets { get; set; } = [];
}

public class DiscoveredCmdlet
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<DiscoveredParameter> Parameters { get; set; } = [];

    // ── V2 fields (from enhanced IntrospectModule.ps1) ─────────
    public bool HasExecIn { get; set; } = true;
    public bool HasExecOut { get; set; } = true;
    public string? PrimaryPipelineParameter { get; set; }
    public List<DataOutputDef> DataOutputs { get; set; } = [];

    /// <summary>Parameter sets this cmdlet declares (excluding __AllParameterSets).</summary>
    public List<string> KnownParameterSets { get; set; } = [];

    /// <summary>Cmdlet's declared default parameter set (may be null).</summary>
    public string? DefaultParameterSet { get; set; }
}

public class DiscoveredParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "String";
    public bool IsMandatory { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] ValidValues { get; set; } = [];

    /// <summary>V2: <c>[Parameter(ValueFromPipeline)]</c> — promotes the paired pin to primary pipeline target.</summary>
    public bool IsPipelineInput { get; set; }

    /// <summary>Sets this param belongs to (empty = all sets).</summary>
    public List<string> ParameterSets { get; set; } = [];

    /// <summary>Sets in which this param is mandatory (overrides IsMandatory when non-empty).</summary>
    public List<string> MandatoryInSets { get; set; } = [];
}
