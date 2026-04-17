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
/// Loads node templates from JSON files in Templates/Builtin/ and Templates/Custom/.
/// Malformed files are skipped with a debug trace.
/// </summary>
public static class TemplateLoader
{
    // Copy from the source-gen context's baked options so the camelCase
    // PropertyNamingPolicy (and string-enum converter) flow through. Just
    // setting TypeInfoResolver on a bare JsonSerializerOptions attaches
    // the resolver but drops the attribute-declared policies.
    private static readonly JsonSerializerOptions Options = new(PblxJsonContext.Default.Options);

    private static readonly JsonSerializerOptions WriteOptions = new(PblxJsonContext.Default.Options)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Cmdlet name → editions (<c>"pwsh"</c> / <c>"powershell"</c>) the catalog was
    /// introspected against. Built during <see cref="LoadAll"/> from each catalog's
    /// <see cref="TemplateCatalogDto.IntrospectedHosts"/> entries. Consumed by the
    /// Run-button mismatch check.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> CmdletEditions { get; private set; }
        = new Dictionary<string, IReadOnlySet<string>>();

    public static List<NodeTemplate> LoadAll()
    {
        var baseDir = AppContext.BaseDirectory;
        var builtinDir = Path.Combine(baseDir, "Templates", "Builtin");
        var customDir = Path.Combine(baseDir, "Templates", "Custom");

        var templates = new List<NodeTemplate>();
        var editions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        LoadFromDirectory(builtinDir, templates, editions);
        LoadFromDirectory(customDir, templates, editions);

        CmdletEditions = editions.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);

        return templates;
    }

    private static int LoadFromDirectory(
        string dir,
        List<NodeTemplate> templates,
        Dictionary<string, HashSet<string>> editionsAccumulator)
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

                // Split "pwsh-7.4.1" / "powershell-5.1" → "pwsh" / "powershell".
                // Unknown shapes are dropped rather than treated as an edition.
                var catalogEditions = catalog.IntrospectedHosts
                    .Select(h => h.Split('-', 2)[0])
                    .Where(e => e is "pwsh" or "powershell")
                    .ToList();

                // v≤2: IsSwitch didn't exist, so every Bool param is a switch
                // (that's the only way they were serialized — typed [bool] cmdlet
                // params are vanishingly rare in the shipping catalogs). Flip the
                // flag in place so downstream consumers see the post-split shape.
                if (catalog.Version < 3)
                {
                    foreach (var t in catalog.Templates)
                        foreach (var p in t.Parameters)
                            if (p.Type == PoSHBlox.Models.ParamType.Bool)
                                p.IsSwitch = true;
                }

                foreach (var t in catalog.Templates)
                {
                    t.Category = catalog.Category;
                    // Derive palette tags from the Verb-Noun if they weren't
                    // already set by the introspector / catalog author.
                    if (t.Tags.Count == 0)
                        t.Tags = [..PaletteTaxonomy.DeriveTags(t.CmdletName)];
                    templates.Add(t);

                    // Prefer per-cmdlet SupportedEditions (finer-grained, set by
                    // IntrospectionMerger) over catalog-level IntrospectedHosts.
                    // Falling back to catalog editions keeps pre-Phase-3 catalogs
                    // working without any warning flood.
                    var cmdletEditions = t.SupportedEditions.Count > 0
                        ? t.SupportedEditions
                        : catalogEditions;

                    if (!string.IsNullOrEmpty(t.CmdletName) && cmdletEditions.Count > 0)
                    {
                        if (!editionsAccumulator.TryGetValue(t.CmdletName, out var set))
                            editionsAccumulator[t.CmdletName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var e in cmdletEditions)
                            set.Add(e);
                    }
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
