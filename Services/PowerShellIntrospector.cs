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
/// Launches PowerShell 5.1 to introspect a module's cmdlets and parameters.
/// </summary>
public static class PowerShellIntrospector
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<IntrospectionResult> IntrospectModuleAsync(string moduleName)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "IntrospectModule.ps1");

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Introspection script not found.", scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
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
}

public class DiscoveredParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "String";
    public bool IsMandatory { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] ValidValues { get; set; } = [];
}
