using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PoSHBlox.Services;

/// <summary>
/// One installed PowerShell host — the executable name, its edition, and the
/// exact version string captured by running <c>$PSVersionTable.PSVersion</c>.
/// </summary>
public sealed record PowerShellHost
{
    /// <summary>Stable identifier used for catalog stamps and settings (e.g. <c>"pwsh-7.4.1"</c>, <c>"powershell-5.1.22621"</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Executable name passed to <see cref="ProcessStartInfo.FileName"/> — resolved against PATH at launch time.</summary>
    public required string Executable { get; init; }

    /// <summary>Full PSVersion from <c>$PSVersionTable</c> (e.g. <c>"7.4.1"</c>).</summary>
    public required string Version { get; init; }

    /// <summary>Edition slug: <c>"pwsh"</c> for PowerShell 7+ Core, <c>"powershell"</c> for Windows PowerShell 5.1.</summary>
    public required string Edition { get; init; }

    /// <summary>Human-facing label for status-bar chips and menus.</summary>
    public string DisplayName => $"{Edition} {Version}";
}

/// <summary>
/// Detects installed PowerShell hosts, captures versions, and caches the result
/// for the app lifetime. <see cref="Default"/> prefers pwsh 7+ over Windows
/// PowerShell 5.1 — modern modules ship Core assemblies that 5.1 can't load,
/// so starting at pwsh fixes that class of import failure.
///
/// Users who install a new host mid-session must restart PoSHBlox to pick it
/// up; the PATH-probe + version query runs once at first access and never
/// re-runs.
/// </summary>
public static class PowerShellHostRegistry
{
    private static readonly Lazy<IReadOnlyList<PowerShellHost>> _all = new(DetectAll);

    /// <summary>All detected hosts, in preference order (pwsh first when present).</summary>
    public static IReadOnlyList<PowerShellHost> All => _all.Value;

    /// <summary>Preferred host for introspection and Run. Null if no host is detected.</summary>
    public static PowerShellHost? Default => All.Count > 0 ? All[0] : null;

    /// <summary>Look up by <see cref="PowerShellHost.Id"/>; returns null when absent.</summary>
    public static PowerShellHost? ById(string id) =>
        All.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>First detected host of a given edition (<c>"pwsh"</c> or <c>"powershell"</c>).</summary>
    public static PowerShellHost? ByEdition(string edition) =>
        All.FirstOrDefault(h => string.Equals(h.Edition, edition, StringComparison.OrdinalIgnoreCase));

    private static List<PowerShellHost> DetectAll()
    {
        var list = new List<PowerShellHost>();

        // pwsh first — it becomes Default when present.
        TryAdd(list, exe: "pwsh.exe", edition: "pwsh");
        TryAdd(list, exe: "powershell.exe", edition: "powershell");

        foreach (var host in list)
            Console.Out.WriteLine($"[host] detected {host.DisplayName} ({host.Executable})");

        return list;
    }

    private static void TryAdd(List<PowerShellHost> list, string exe, string edition)
    {
        if (!LocateOnPath(exe)) return;
        if (!TryQueryVersion(exe, out var version)) return;
        list.Add(new PowerShellHost
        {
            Id = $"{edition}-{version}",
            Executable = exe,
            Version = version,
            Edition = edition,
        });
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

    private static bool TryQueryVersion(string exe, out string version)
    {
        version = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5000))
            {
                try { p.Kill(); } catch { }
                return false;
            }
            version = output.Trim();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }
}
