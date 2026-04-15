using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Developer tool: regenerate a Templates/Builtin/*.json catalog from a live
/// PowerShell module via the V2 introspector. Invoked from Program.Main when
/// the app is launched with <c>--regen-builtin</c> (no GUI).
///
/// Curated per-parameter <c>defaultValue</c>s in the existing output file are
/// preserved across runs — cmdlets/params matched by name keep their old
/// defaults, giving you a safe round-trip after re-scanning.
/// </summary>
public static class TemplateRegenerator
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public class Options
    {
        public required string ModuleName { get; init; }
        public required string OutputPath { get; init; }
        public string? CategoryName { get; init; }
        public HashSet<string>? OnlyCmdlets { get; init; }
    }

    /// <summary>
    /// Run the regen pipeline. Returns process exit code: 0 on success, non-zero
    /// on failure (with a message printed to stderr).
    /// </summary>
    public static async Task<int> RegenerateAsync(Options opts)
    {
        // Resolve category: explicit --category wins, else reuse the one already
        // on the file, else fail — we won't guess it from the module name.
        string? existingCategory = null;
        TemplateCatalogDto? existing = null;
        if (File.Exists(opts.OutputPath))
        {
            try
            {
                existing = JsonSerializer.Deserialize<TemplateCatalogDto>(
                    await File.ReadAllTextAsync(opts.OutputPath), ReadOptions);
                existingCategory = existing?.Category;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[regen] warning: could not read existing {opts.OutputPath}: {ex.Message}");
            }
        }

        var category = opts.CategoryName ?? existingCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            Console.Error.WriteLine($"[regen] error: --category required (no existing file at {opts.OutputPath} to inherit from).");
            return 2;
        }

        Console.Out.WriteLine($"[regen] scanning module '{opts.ModuleName}'...");
        IntrospectionResult result;
        try
        {
            result = await PowerShellIntrospector.IntrospectModuleAsync(opts.ModuleName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[regen] introspection failed: {ex.Message}");
            return 3;
        }

        // Filter if --only was supplied.
        var cmdlets = opts.OnlyCmdlets is { Count: > 0 }
            ? result.Cmdlets.Where(c => opts.OnlyCmdlets.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList()
            : result.Cmdlets;

        if (cmdlets.Count == 0)
        {
            Console.Error.WriteLine($"[regen] no cmdlets matched {{module='{opts.ModuleName}', only={FormatOnly(opts.OnlyCmdlets)}}}.");
            return 4;
        }

        // Build the preservation map: (templateName,paramName) → defaultValue from old file.
        var preservedDefaults = BuildPreservationMap(existing);
        int preserved = 0;

        var catalog = new TemplateCatalogDto
        {
            Version = 2,
            Category = category,
        };

        foreach (var cmdlet in cmdlets)
        {
            var tpl = new NodeTemplate
            {
                Name = cmdlet.Name,
                CmdletName = cmdlet.Name,
                Description = cmdlet.Description,
                HasExecIn = cmdlet.HasExecIn,
                HasExecOut = cmdlet.HasExecOut,
                PrimaryPipelineParameter = cmdlet.PrimaryPipelineParameter,
                DataOutputs = cmdlet.DataOutputs.Count > 0
                    ? cmdlet.DataOutputs
                    : [new DataOutputDef { Name = "Out", Type = ParamType.Any, IsPrimary = true }],
            };

            foreach (var p in cmdlet.Parameters)
            {
                var paramType = Enum.TryParse<ParamType>(p.Type, out var pt) ? pt : ParamType.String;
                var def = new ParameterDef
                {
                    Name = p.Name,
                    Type = paramType,
                    IsMandatory = p.IsMandatory,
                    DefaultValue = p.DefaultValue,
                    Description = p.Description,
                    ValidValues = p.ValidValues ?? [],
                    IsPipelineInput = p.IsPipelineInput,
                };

                // Re-apply curated default from old file if it had one and the
                // introspector didn't supply a non-empty value of its own.
                if (string.IsNullOrEmpty(def.DefaultValue)
                    && preservedDefaults.TryGetValue((cmdlet.Name, p.Name), out var prior)
                    && !string.IsNullOrEmpty(prior))
                {
                    def.DefaultValue = prior;
                    preserved++;
                }

                tpl.Parameters.Add(def);
            }

            catalog.Templates.Add(tpl);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(opts.OutputPath))!);
        var json = JsonSerializer.Serialize(catalog, WriteOptions);
        await File.WriteAllTextAsync(opts.OutputPath, json);

        Console.Out.WriteLine(
            $"[regen] wrote {catalog.Templates.Count} cmdlet(s) to {opts.OutputPath}  "
          + $"(category='{category}', preserved-defaults={preserved})");
        return 0;
    }

    private static Dictionary<(string, string), string> BuildPreservationMap(TemplateCatalogDto? existing)
    {
        var map = new Dictionary<(string, string), string>();
        if (existing?.Templates == null) return map;
        foreach (var t in existing.Templates)
            foreach (var p in t.Parameters)
                map[(t.Name, p.Name)] = p.DefaultValue;
        return map;
    }

    private static string FormatOnly(HashSet<string>? only)
        => only is { Count: > 0 } ? string.Join(",", only) : "(all)";
}
