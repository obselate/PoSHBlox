using System;
using System.Collections.Generic;
using System.Linq;

namespace PoSHBlox.Services;

/// <summary>
/// Folds multiple single-host <see cref="IntrospectionResult"/>s into one merged
/// cmdlet list where each cmdlet and parameter carries a <c>SupportedEditions</c>
/// list reflecting which hosts it was observed in. Lets downstream consumers
/// flag an edition-missing parameter without re-running the introspector.
///
/// Merge rules (keep it boring and deterministic):
/// <list type="bullet">
/// <item>Cmdlets are keyed by <c>Name</c>, case-insensitive.</item>
/// <item>The first result contributes the base record; subsequent results layer
///       on their edition into <c>SupportedEditions</c> and merge parameters.</item>
/// <item>Fields like <c>Description</c>, <c>DataOutputs</c>, <c>KnownParameterSets</c>
///       come from the first result that had the cmdlet — we prefer <c>pwsh</c>
///       by passing results in that order from the registry.</item>
/// <item>Parameters are keyed by <c>Name</c>, case-insensitive. Multi-edition
///       params union <c>ParameterSets</c> / <c>MandatoryInSets</c> / <c>ValidValues</c>
///       so a set declared only on 5.1 still shows up in the catalog.</item>
/// </list>
/// </summary>
public static class IntrospectionMerger
{
    /// <summary>
    /// Merge per-host results keyed by <see cref="PowerShellHost.Edition"/>.
    /// Input must be a list of <c>(edition, result)</c> pairs — the edition
    /// drives <c>SupportedEditions</c> stamps. Callers pass the active host's
    /// edition alongside its <see cref="IntrospectionResult"/>.
    /// </summary>
    public static List<DiscoveredCmdlet> Merge(IReadOnlyList<(string Edition, IntrospectionResult Result)> perHost)
    {
        var merged = new Dictionary<string, DiscoveredCmdlet>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var (edition, result) in perHost)
        {
            foreach (var cmdlet in result.Cmdlets)
            {
                if (!merged.TryGetValue(cmdlet.Name, out var existing))
                {
                    existing = Clone(cmdlet);
                    merged[cmdlet.Name] = existing;
                    order.Add(cmdlet.Name);
                }

                if (!existing.SupportedEditions.Contains(edition, StringComparer.OrdinalIgnoreCase))
                    existing.SupportedEditions.Add(edition);

                MergeParameters(existing, cmdlet, edition);
            }
        }

        return order.Select(n => merged[n]).ToList();
    }

    private static void MergeParameters(DiscoveredCmdlet into, DiscoveredCmdlet from, string edition)
    {
        var byName = into.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var p in from.Parameters)
        {
            if (!byName.TryGetValue(p.Name, out var existing))
            {
                existing = CloneParam(p);
                into.Parameters.Add(existing);
                byName[p.Name] = existing;
            }
            else
            {
                existing.ParameterSets = existing.ParameterSets
                    .Union(p.ParameterSets, StringComparer.OrdinalIgnoreCase).ToList();
                existing.MandatoryInSets = existing.MandatoryInSets
                    .Union(p.MandatoryInSets, StringComparer.OrdinalIgnoreCase).ToList();
                if ((existing.ValidValues?.Length ?? 0) == 0 && (p.ValidValues?.Length ?? 0) > 0)
                    existing.ValidValues = p.ValidValues;
                if (string.IsNullOrEmpty(existing.Description) && !string.IsNullOrEmpty(p.Description))
                    existing.Description = p.Description;
            }

            if (!existing.SupportedEditions.Contains(edition, StringComparer.OrdinalIgnoreCase))
                existing.SupportedEditions.Add(edition);
        }
    }

    private static DiscoveredCmdlet Clone(DiscoveredCmdlet src) => new()
    {
        Name = src.Name,
        Description = src.Description,
        HasExecIn = src.HasExecIn,
        HasExecOut = src.HasExecOut,
        PrimaryPipelineParameter = src.PrimaryPipelineParameter,
        DataOutputs = new List<DataOutputDef>(src.DataOutputs),
        KnownParameterSets = new List<string>(src.KnownParameterSets),
        DefaultParameterSet = src.DefaultParameterSet,
        Parameters = src.Parameters.Select(CloneParam).ToList(),
        SupportedEditions = [],
    };

    private static DiscoveredParameter CloneParam(DiscoveredParameter src) => new()
    {
        Name = src.Name,
        Type = src.Type,
        IsMandatory = src.IsMandatory,
        DefaultValue = src.DefaultValue,
        Description = src.Description,
        ValidValues = src.ValidValues ?? [],
        IsPipelineInput = src.IsPipelineInput,
        IsSwitch = src.IsSwitch,
        ParameterSets = new List<string>(src.ParameterSets),
        MandatoryInSets = new List<string>(src.MandatoryInSets),
        SupportedEditions = [],
    };
}
