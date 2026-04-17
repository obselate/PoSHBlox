using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PoSHBlox.Services;

/// <summary>
/// Launches <c>Get-Module -ListAvailable</c> via <see cref="PowerShellHostRegistry.All"/>
/// in parallel, then folds the per-host results into one list keyed by module
/// name. Each merged row carries the editions it was seen in so the Import UX
/// can badge edition-exclusive modules without another scan. Used by the
/// <see cref="ViewModels.ImportModuleViewModel"/> picker.
/// </summary>
public static class PowerShellModuleDiscovery
{
    private static readonly JsonSerializerOptions Options = new(PblxJsonContext.Default.Options);

    public readonly record struct Result(
        IReadOnlyList<AvailableModule> Modules,
        IReadOnlyList<string> HostErrors);

    /// <summary>
    /// Discover modules across every detected host. Hosts whose probes fail
    /// are skipped; their error messages come back in <see cref="Result.HostErrors"/>
    /// so the UI can surface partial failures without hiding successes.
    /// </summary>
    public static async Task<Result> DiscoverAsync()
    {
        var hosts = PowerShellHostRegistry.All;
        if (hosts.Count == 0)
            return new Result([], ["No PowerShell host detected on PATH."]);

        var tasks = hosts
            .Select(async h =>
            {
                try
                {
                    var modules = await QueryHostAsync(h);
                    return (host: h, modules, error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (host: h, modules: (List<DiscoveredModuleRaw>?)null, error: $"{h.DisplayName}: {ex.Message}");
                }
            })
            .ToList();

        var outcomes = await Task.WhenAll(tasks);

        var merged = new Dictionary<string, AvailableModule>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var (host, modules, error) in outcomes)
        {
            if (modules == null)
            {
                if (error != null) errors.Add(error);
                continue;
            }

            foreach (var m in modules)
            {
                if (string.IsNullOrWhiteSpace(m.Name)) continue;

                if (!merged.TryGetValue(m.Name, out var existing))
                {
                    existing = new AvailableModule
                    {
                        Name = m.Name,
                        Version = m.Version ?? "",
                        Description = m.Description ?? "",
                    };
                    merged[m.Name] = existing;
                }
                else
                {
                    // Prefer the later (higher-edition) version string when a
                    // module ships different versions per host — gives a sane
                    // label without misleading precision.
                    if (CompareVersionLoose(m.Version, existing.Version) > 0)
                        existing.Version = m.Version ?? existing.Version;
                    if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(m.Description))
                        existing.Description = m.Description!;
                }

                if (!existing.FoundInEditions.Contains(host.Edition, StringComparer.OrdinalIgnoreCase))
                    existing.FoundInEditions.Add(host.Edition);
            }
        }

        var sorted = merged.Values
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Result(sorted, errors);
    }

    private static async Task<List<DiscoveredModuleRaw>> QueryHostAsync(PowerShellHost host)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "ListAvailableModules.ps1");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Module-discovery script not found.", scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = host.Executable,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {host.Executable}.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Non-zero exit." : stderr.Trim());

        if (string.IsNullOrWhiteSpace(stdout))
            return [];

        return JsonSerializer.Deserialize<List<DiscoveredModuleRaw>>(stdout, Options) ?? [];
    }

    private static int CompareVersionLoose(string? a, string? b)
    {
        // Good enough for display-preference: prefer the longer / lexically
        // greater version string. Not a strict SemVer comparison — a module
        // shipping "2.0.0" beats "1.99" here (both SemVer and our lexical
        // compare agree on the common cases).
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);
        return string.Compare(a, b, StringComparison.Ordinal);
    }
}

/// <summary>Raw deserialization target — matches the JSON emitted by ListAvailableModules.ps1.</summary>
public sealed class DiscoveredModuleRaw
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// One module row surfaced in the Import picker. Merged across hosts so we
/// know which editions expose it — used by the UI to badge edition-exclusive
/// modules.
/// </summary>
public sealed class AvailableModule
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Editions the module was seen in (e.g. <c>["pwsh", "powershell"]</c>).</summary>
    public List<string> FoundInEditions { get; set; } = [];

    /// <summary>
    /// Set only when the module is exclusive to one edition — drives the
    /// right-aligned badge in the picker. Null when available on both.
    /// </summary>
    public string? ExclusiveEdition =>
        FoundInEditions.Count == 1 ? FoundInEditions[0] : null;

    /// <summary>True when both pwsh and powershell detected this module (no badge).</summary>
    public bool IsUniversal => FoundInEditions.Count > 1;

    /// <summary>Human-readable badge text — "pwsh only" / "5.1 only", empty when universal.</summary>
    public string BadgeText => ExclusiveEdition switch
    {
        "pwsh" => "pwsh only",
        "powershell" => "5.1 only",
        _ => "",
    };

    /// <summary>Drives badge visibility. False for universal / unknown modules.</summary>
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);
}
