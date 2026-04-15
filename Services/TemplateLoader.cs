using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PoSHBlox.Models;

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
                    NormalizeV1Fallback(t);
                    // Derive palette tags from the Verb-Noun if they weren't
                    // already set by the introspector / catalog author.
                    if (t.Tags.Count == 0)
                        t.Tags = [..PaletteTaxonomy.DeriveTags(t.CmdletName)];
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

    /// <summary>
    /// Derive V2 shape from V1-only JSON files so shipped Templates/Builtin/*.json
    /// produce sensible nodes until they're regenerated via the V2 introspector.
    /// No-op when the template already has V2 fields set explicitly.
    /// </summary>
    private static void NormalizeV1Fallback(NodeTemplate t)
    {
        // V1 JSONs didn't carry hasExecIn — InputCount == 0 meant "no pipeline input".
        // Map that to HasExecIn=false so nodes like Get-Process (source nodes) don't
        // get a useless exec-in triangle.
        if (t.InputCount == 0) t.HasExecIn = false;

        // V1 OutputCount==0 → terminal cmdlet (no data out). Keep HasExecOut=true so
        // exec still chains forward. V1 OutputCount>=1 + empty DataOutputs → synthesize.
        if (t.DataOutputs.Count == 0 && t.OutputCount > 0)
        {
            for (int i = 0; i < t.OutputCount; i++)
            {
                var name = i < t.OutputNames.Length && !string.IsNullOrWhiteSpace(t.OutputNames[i])
                    ? t.OutputNames[i]
                    : $"Out{(i == 0 ? "" : (i + 1).ToString())}";
                t.DataOutputs.Add(new DataOutputDef
                {
                    Name = name,
                    Type = ParamType.Any,
                    IsPrimary = i == 0,
                });
            }
        }

        // If no IsPipelineInput flag was present in the JSON but a parameter is
        // named the same as the template's primaryPipelineParameter (none in V1
        // JSONs), promote it. Otherwise leave as-is.
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
