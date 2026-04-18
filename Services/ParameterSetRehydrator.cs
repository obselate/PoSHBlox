using System;
using System.Collections.Generic;
using System.Linq;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Backfills <see cref="GraphNode.KnownParameterSets"/> / <see cref="GraphNode.ActiveParameterSet"/>
/// from the template catalog for any loaded node that arrived without set
/// metadata — covers V1 files (which pre-date the fields entirely) and any V2
/// file saved before rehydration existed. Without this, pin-visibility and the
/// properties-panel set picker stay silent and every parameter renders at once.
/// Runs after graph rebuild so live <see cref="GraphNode"/>s are patched in
/// place; caller re-runs the downstream visibility / validation refreshes.
/// </summary>
public static class ParameterSetRehydrator
{
    public static void Rehydrate(IEnumerable<GraphNode> nodes)
    {
        var lookup = BuildLookup();
        if (lookup.Count == 0) return;

        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.CmdletName)) continue;
            if (node.KnownParameterSets.Length > 0) continue;
            if (!lookup.TryGetValue(node.CmdletName, out var info)) continue;

            node.KnownParameterSets = info.KnownSets;
            if (string.IsNullOrEmpty(node.ActiveParameterSet))
                node.ActiveParameterSet = info.DefaultSet;
        }
    }

    private static Dictionary<string, SetInfo> BuildLookup()
    {
        var map = new Dictionary<string, SetInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TemplateLoader.LoadAll())
        {
            if (string.IsNullOrEmpty(t.CmdletName) || t.KnownParameterSets.Count == 0) continue;
            // Later entries win — matches LoadAll's builtin-then-custom load
            // order, so user overrides take precedence over shipped catalogs.
            map[t.CmdletName] = new SetInfo(
                KnownSets:  t.KnownParameterSets.ToArray(),
                DefaultSet: t.DefaultParameterSet ?? t.KnownParameterSets[0]);
        }
        return map;
    }

    private readonly record struct SetInfo(string[] KnownSets, string DefaultSet);
}
