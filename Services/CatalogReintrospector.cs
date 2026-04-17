using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PoSHBlox.Services;

/// <summary>
/// Re-runs the introspection pipeline against every <c>Templates/Custom/*.json</c>
/// so user-imported catalogs pick up per-param <c>SupportedEditions</c> after a
/// host has been installed / switched / upgraded. Preserves each catalog's
/// cmdlet selection (re-scan is scoped to the cmdlets already in the catalog)
/// and its curated defaults.
/// </summary>
public static class CatalogReintrospector
{
    private static readonly JsonSerializerOptions ReadOptions = new(PblxJsonContext.Default.Options);

    public readonly record struct Report(int Succeeded, int Failed, int Skipped, List<string> Messages);

    /// <summary>
    /// Scan every Custom catalog. Returns a summary the UI can surface. One
    /// catalog's failure doesn't stop the rest — the user can decide per-module
    /// later. Built-in catalogs are deliberately untouched; the app ships those
    /// and users don't own them.
    /// </summary>
    public static async Task<Report> RunAsync()
    {
        var customDir = Path.Combine(AppContext.BaseDirectory, "Templates", "Custom");
        var messages = new List<string>();

        if (!Directory.Exists(customDir))
            return new Report(0, 0, 0, ["No Templates/Custom directory — nothing to re-introspect."]);

        var files = Directory.GetFiles(customDir, "*.json");
        if (files.Length == 0)
            return new Report(0, 0, 0, ["No custom catalogs found."]);

        int ok = 0, failed = 0, skipped = 0;

        foreach (var file in files)
        {
            TemplateCatalogDto? existing = null;
            try
            {
                existing = JsonSerializer.Deserialize<TemplateCatalogDto>(
                    await File.ReadAllTextAsync(file), ReadOptions);
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"Skipped {Path.GetFileName(file)}: parse error — {ex.Message}");
                continue;
            }

            if (existing == null || string.IsNullOrWhiteSpace(existing.Category))
            {
                skipped++;
                messages.Add($"Skipped {Path.GetFileName(file)}: no category to treat as module name.");
                continue;
            }

            var moduleName = existing.Category.Trim();
            var onlyCmdlets = existing.Templates
                .Select(t => t.CmdletName)
                .Where(c => !string.IsNullOrEmpty(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var opts = new TemplateRegenerator.Options
            {
                ModuleName = moduleName,
                OutputPath = file,
                CategoryName = existing.Category,
                OnlyCmdlets = onlyCmdlets.Count > 0 ? onlyCmdlets : null,
            };

            int rc;
            try
            {
                rc = await TemplateRegenerator.RegenerateAsync(opts);
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"Failed {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            if (rc == 0)
            {
                ok++;
                messages.Add($"Rescanned {Path.GetFileName(file)}.");
            }
            else
            {
                failed++;
                messages.Add($"Regen returned exit {rc} for {Path.GetFileName(file)}.");
            }
        }

        return new Report(ok, failed, skipped, messages);
    }
}
