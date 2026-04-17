using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PoSHBlox.Services;

/// <summary>
/// App-wide persisted preferences, keyed by a tiny JSON file in
/// <see cref="Environment.SpecialFolder.ApplicationData"/>/<c>PoSHBlox/settings.json</c>.
/// Read once at first access, written on every mutation. Corrupt / missing files
/// fall back to defaults — user shouldn't see a crash because an old settings
/// file disagreed with a newer schema.
/// </summary>
public static class AppSettings
{
    private static readonly object Lock = new();
    private static AppSettingsData? _data;

    private static readonly JsonSerializerOptions Options = new(PblxJsonContext.Default.Options)
    {
        WriteIndented = true,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoSHBlox",
        "settings.json");

    /// <summary>
    /// Id of the user-preferred host (e.g. <c>"pwsh-7.4.1"</c>). Consumed on startup
    /// by <see cref="PowerShellHostRegistry"/> — when the id still matches a detected
    /// host, that host wins over <see cref="PowerShellHostRegistry.Default"/>. If the
    /// saved host is gone (uninstalled / machine change), the registry silently falls
    /// back to the default.
    /// </summary>
    public static string? PreferredHostId
    {
        get => Load().PreferredHostId;
        set
        {
            lock (Lock)
            {
                var data = Load();
                if (string.Equals(data.PreferredHostId, value, StringComparison.OrdinalIgnoreCase)) return;
                data.PreferredHostId = value;
                Save(data);
            }
        }
    }

    private static AppSettingsData Load()
    {
        if (_data != null) return _data;
        lock (Lock)
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _data = JsonSerializer.Deserialize<AppSettingsData>(json, Options) ?? new();
                    return _data;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to read {FilePath}: {ex.Message}. Using defaults.");
            }
            _data = new AppSettingsData();
            return _data;
        }
    }

    private static void Save(AppSettingsData data)
    {
        _data = data;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(data, Options);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettings] Failed to write {FilePath}: {ex.Message}");
        }
    }
}

public sealed class AppSettingsData
{
    public string? PreferredHostId { get; set; }
}
