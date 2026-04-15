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
/// Prefers PowerShell 7 (pwsh.exe) when available, falls back to Windows
/// PowerShell 5.1 (powershell.exe). Many modern modules ship .NET Core
/// assemblies that 5.1 can't load — hitting pwsh first fixes that class
/// of failure (Microsoft.PowerShell.Security on 5.1 is the canonical
/// case where the .dll is built for net5+ and the 5.1 import errors).
/// </summary>
public static class PowerShellIntrospector
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? _resolvedHost;

    /// <summary>
    /// Resolve the PowerShell host to use. pwsh.exe on PATH wins; falls back
    /// to powershell.exe for environments without PS 7 installed. Cached so
    /// repeated invocations (e.g. manifest regen across many modules) don't
    /// re-probe the PATH.
    /// </summary>
    private static string ResolvePowerShellHost()
    {
        if (_resolvedHost != null) return _resolvedHost;
        _resolvedHost = LocateOnPath("pwsh.exe") ? "pwsh.exe" : "powershell.exe";
        Console.Out.WriteLine($"[regen]   using host '{_resolvedHost}'");
        return _resolvedHost;
    }

    private static bool LocateOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, exe)))
                    return true;
            }
            catch { /* ignore invalid PATH entries */ }
        }
        return false;
    }

    public static async Task<IntrospectionResult> IntrospectModuleAsync(string moduleName)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "IntrospectModule.ps1");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Introspection script not found.", scriptPath);

        var host = ResolvePowerShellHost();
        var psi = new ProcessStartInfo
        {
            FileName = host,
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
            return new IntrospectionResult { ResolvedModuleName = resolvedName, Cmdlets = [] };

        var cmdlets = JsonSerializer.Deserialize<List<DiscoveredCmdlet>>(stdout, Options) ?? [];
        return new IntrospectionResult { ResolvedModuleName = resolvedName, Cmdlets = cmdlets };
    }
}

public class IntrospectionResult
{
    public string ResolvedModuleName { get; set; } = "";
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
