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
/// Developer tool: regenerate Templates/Builtin/*.json catalogs from live
/// PowerShell modules via the V2 introspector. Invoked from Program.Main
/// with <c>--regen-builtin</c> (single file) or <c>--regen-manifest</c>
/// (batch from a JSON manifest). GUI is never spawned.
///
/// Curated per-parameter <c>defaultValue</c>s in the existing output file
/// are preserved across runs — cmdlets/params matched by name keep their
/// old defaults, giving safe round-trips after re-scanning.
/// </summary>
public static class TemplateRegenerator
{
    // Copy from the source-gen context's baked options so PropertyNamingPolicy
    // (camelCase) and the string-enum converter carry through. Setting only
    // TypeInfoResolver on a fresh JsonSerializerOptions attaches the resolver
    // but drops the policy — deserialize then fails to map JSON keys like
    // "targets" to C# Targets and the manifest load silently returns empty.
    private static readonly JsonSerializerOptions ReadOptions = new(PblxJsonContext.Default.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new(PblxJsonContext.Default.Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public class Options
    {
        public required string ModuleName { get; init; }
        public required string OutputPath { get; init; }
        public string? CategoryName { get; init; }
        public HashSet<string>? OnlyCmdlets { get; init; }
    }

    // ── Single-target entry (used by --regen-builtin) ───────────

    /// <summary>
    /// Run the single-module regen pipeline. Returns process exit code:
    /// 0 on success, non-zero on failure.
    /// </summary>
    public static async Task<int> RegenerateAsync(Options opts, bool dryRun = false)
    {
        var (existing, existingCategory) = await LoadExistingAsync(opts.OutputPath);

        var category = opts.CategoryName ?? existingCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            Console.Error.WriteLine($"[regen] error: --category required (no existing file at {opts.OutputPath} to inherit from).");
            return 2;
        }

        var cmdletsResult = await IntrospectAndFilterAsync(opts.ModuleName, opts.OnlyCmdlets);
        if (cmdletsResult.ExitCode != 0) return cmdletsResult.ExitCode;

        var hosts = cmdletsResult.HostId is { Length: > 0 } ? new List<string> { cmdletsResult.HostId } : [];
        return await WriteCatalogAsync(opts.OutputPath, category, cmdletsResult.Cmdlets, hosts, existing, dryRun);
    }

    // ── Manifest entry (used by --regen-manifest) ──────────────

    /// <summary>
    /// Read a manifest JSON file and regenerate every target listed. Each target
    /// can pull cmdlets from multiple source modules and merge them into one
    /// catalog file — lets a single Builtin category (e.g. NetworkRemote) gather
    /// from Microsoft.PowerShell.Core + Microsoft.PowerShell.Utility + NetTCPIP
    /// in one pass. Curated defaults survive via the same preservation logic.
    /// </summary>
    public static async Task<int> RegenerateFromManifestAsync(string manifestPath, bool dryRun)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"[regen-manifest] file not found: {manifestPath}");
            return 1;
        }

        RegenManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RegenManifest>(
                await File.ReadAllTextAsync(manifestPath), ReadOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[regen-manifest] parse failed: {ex.Message}");
            return 1;
        }

        if (manifest?.Targets == null || manifest.Targets.Count == 0)
        {
            Console.Error.WriteLine($"[regen-manifest] no targets in {manifestPath}");
            return 1;
        }

        Console.Out.WriteLine($"[regen-manifest] {manifest.Targets.Count} target(s) from {manifestPath}{(dryRun ? " (dry run)" : "")}");
        if (!string.IsNullOrWhiteSpace(manifest.Description))
            Console.Out.WriteLine($"[regen-manifest] {manifest.Description}");

        int ok = 0, failed = 0;
        foreach (var target in manifest.Targets)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine($"[regen-manifest] → {target.OutputFile}");

            int rc;
            try
            {
                rc = await RegenerateTargetAsync(target, dryRun);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[regen-manifest]   unhandled exception: {ex.Message}");
                rc = 99;
            }

            if (rc == 0) ok++;
            else failed++;
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"[regen-manifest] done: {ok} ok, {failed} failed.");
        return failed == 0 ? 0 : 2;
    }

    private static async Task<int> RegenerateTargetAsync(RegenTarget target, bool dryRun)
    {
        if (target.Sources == null || target.Sources.Count == 0)
        {
            Console.Error.WriteLine($"[regen-manifest]   no sources for {target.OutputFile}");
            return 5;
        }

        var (existing, existingCategory) = await LoadExistingAsync(target.OutputFile);
        var category = target.Category ?? existingCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            Console.Error.WriteLine($"[regen-manifest]   category required for {target.OutputFile} (no existing file to inherit from)");
            return 2;
        }

        // Introspect every source module, filter each, then merge. Later sources
        // can override earlier ones if the same cmdlet name appears — last write wins.
        var merged = new List<DiscoveredCmdlet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hosts = new List<string>();
        foreach (var source in target.Sources)
        {
            var only = source.Cmdlets is { Count: > 0 }
                ? new HashSet<string>(source.Cmdlets, StringComparer.OrdinalIgnoreCase)
                : null;
            var result = await IntrospectAndFilterAsync(source.Module, only);
            if (result.ExitCode != 0) return result.ExitCode;

            if (result.HostId is { Length: > 0 } && !hosts.Contains(result.HostId))
                hosts.Add(result.HostId);

            foreach (var c in result.Cmdlets)
            {
                if (seen.Add(c.Name)) merged.Add(c);
            }
        }

        if (merged.Count == 0)
        {
            Console.Error.WriteLine($"[regen-manifest]   no cmdlets matched any source for {target.OutputFile}");
            return 4;
        }

        return await WriteCatalogAsync(target.OutputFile, category, merged, hosts, existing, dryRun);
    }

    // ── Shared pipeline pieces ─────────────────────────────────

    private static async Task<(TemplateCatalogDto? Existing, string? Category)> LoadExistingAsync(string path)
    {
        if (!File.Exists(path)) return (null, null);
        try
        {
            var dto = JsonSerializer.Deserialize<TemplateCatalogDto>(
                await File.ReadAllTextAsync(path), ReadOptions);
            return (dto, dto?.Category);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[regen] warning: could not read existing {path}: {ex.Message}");
            return (null, null);
        }
    }

    private record struct IntrospectResult(int ExitCode, List<DiscoveredCmdlet> Cmdlets, string HostId);

    private static async Task<IntrospectResult> IntrospectAndFilterAsync(string module, HashSet<string>? only)
    {
        Console.Out.WriteLine($"[regen]   scanning '{module}'{(only != null ? $" (only {only.Count} cmdlet(s))" : "")}...");
        IntrospectionResult result;
        try
        {
            result = await PowerShellIntrospector.IntrospectModuleAsync(module);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[regen]   introspection failed for '{module}': {ex.Message}");
            return new IntrospectResult(3, [], "");
        }

        var cmdlets = only is { Count: > 0 }
            ? result.Cmdlets.Where(c => only.Contains(c.Name)).ToList()
            : result.Cmdlets;

        if (only != null && cmdlets.Count < only.Count)
        {
            var missing = only.Except(cmdlets.Select(c => c.Name), StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                Console.Error.WriteLine($"[regen]   warning: {missing.Count} requested cmdlet(s) not found in '{module}': {string.Join(", ", missing)}");
        }

        return new IntrospectResult(0, cmdlets, result.HostId);
    }

    private static async Task<int> WriteCatalogAsync(
        string outputPath,
        string category,
        List<DiscoveredCmdlet> cmdlets,
        List<string> introspectedHosts,
        TemplateCatalogDto? existing,
        bool dryRun)
    {
        var preservedDefaults = BuildPreservationMap(existing);
        int preserved = 0;

        var catalog = new TemplateCatalogDto
        {
            Version = 2,
            Category = category,
            IntrospectedHosts = introspectedHosts,
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
                KnownParameterSets = cmdlet.KnownParameterSets,
                DefaultParameterSet = cmdlet.DefaultParameterSet,
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
                    ParameterSets = p.ParameterSets,
                    MandatoryInSets = p.MandatoryInSets,
                };

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

        var status = $"{catalog.Templates.Count} cmdlet(s), preserved-defaults={preserved}";
        if (dryRun)
        {
            Console.Out.WriteLine($"[regen]   would write {outputPath}  ({status})");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var json = JsonSerializer.Serialize(catalog, WriteOptions);
        await File.WriteAllTextAsync(outputPath, json);

        Console.Out.WriteLine($"[regen]   wrote {outputPath}  ({status})");
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
}

// ── Manifest DTOs ──────────────────────────────────────────────

/// <summary>
/// Top-level manifest driving <c>--regen-manifest</c>. Commit one of these to
/// the repo as the source of truth for what ships in Templates/Builtin/.
/// </summary>
public class RegenManifest
{
    public string? Description { get; set; }
    public List<RegenTarget> Targets { get; set; } = [];
}

/// <summary>One output catalog — collects cmdlets from one or more source modules.</summary>
public class RegenTarget
{
    public required string OutputFile { get; set; }

    /// <summary>Category string stamped into the output catalog. Inherited from the existing file if omitted.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Sources to merge into this catalog. Later sources can contribute different
    /// cmdlets; duplicate cmdlet names (by name) keep the first occurrence.
    /// </summary>
    public List<RegenSource> Sources { get; set; } = [];
}

/// <summary>One scan of one module — optionally filtered to a specific cmdlet list.</summary>
public class RegenSource
{
    public required string Module { get; set; }

    /// <summary>If non-empty, restrict to these cmdlets. Null/empty = everything in the module.</summary>
    public List<string>? Cmdlets { get; set; }
}
