using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PoSHBlox.Services;

/// <summary>
/// Loads node templates from JSON files in Templates/Builtin/ and Templates/Custom/.
/// Malformed files are skipped with a debug trace.
/// </summary>
public static class TemplateLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<NodeTemplate> LoadAll()
    {
        var baseDir = AppContext.BaseDirectory;
        var builtinDir = Path.Combine(baseDir, "Templates", "Builtin");
        var customDir = Path.Combine(baseDir, "Templates", "Custom");

        var templates = new List<NodeTemplate>();

        LoadFromDirectory(builtinDir, templates);
        LoadFromDirectory(customDir, templates);

        return templates;
    }

    private static int LoadFromDirectory(string dir, List<NodeTemplate> templates)
    {
        if (!Directory.Exists(dir)) return 0;

        int count = 0;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var catalog = JsonSerializer.Deserialize<TemplateCatalogDto>(json, Options);
                if (catalog?.Templates == null) continue;

                foreach (var t in catalog.Templates)
                {
                    t.Category = catalog.Category;
                    templates.Add(t);
                }
                count++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateLoader] Skipping malformed file {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return count;
    }

    public static async Task SaveCustomCatalogAsync(TemplateCatalogDto catalog, string categoryName)
    {
        var baseDir = AppContext.BaseDirectory;
        var customDir = Path.Combine(baseDir, "Templates", "Custom");
        Directory.CreateDirectory(customDir);

        var safeName = SanitizeFileName(categoryName);
        var path = Path.Combine(customDir, $"{safeName}.json");

        var json = JsonSerializer.Serialize(catalog, WriteOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = name;
        foreach (var c in invalid)
            sanitized = sanitized.Replace(c, '_');
        return sanitized;
    }
}
