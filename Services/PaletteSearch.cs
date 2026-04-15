using System;
using System.Collections.Generic;
using System.Linq;

namespace PoSHBlox.Services;

/// <summary>
/// Palette-side classification. Pure functions — no state.
///
/// Tags are derived from the PowerShell verb at catalog load. The four
/// buckets answer the questions users actually ask the palette:
///   - "will this change anything?"  → mutate / destroy
///   - "can I run this safely?"      → safe
///   - "does this invoke arbitrary code?" → act
///
/// Verbs not listed in any bucket stay untagged — better to leave a custom
/// cmdlet unlabelled than silently miscategorize it.
/// </summary>
public static class PaletteTaxonomy
{
    private static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "test", "measure", "find", "search", "read", "show", "resolve",
        "select", "compare", "format", "convertto", "convertfrom", "where",
        "sort", "group", "split", "join", "out", "write", "trace",
        "confirm", "sync", "expand",
    };

    private static readonly HashSet<string> Mutate = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "new", "add", "update", "install", "enable", "register",
        "import", "save", "publish", "rename", "copy", "move", "push",
        "merge", "mount", "grant", "protect", "unprotect",
        "initialize", "restore", "submit", "approve",
    };

    private static readonly HashSet<string> Destroy = new(StringComparer.OrdinalIgnoreCase)
    {
        "remove", "uninstall", "disable", "unregister", "clear", "reset",
        "stop", "disconnect", "close", "dismount", "lock", "block",
        "hide", "exit", "pop", "limit", "kill", "revoke", "deny",
    };

    private static readonly HashSet<string> Act = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoke", "start", "restart", "suspend", "resume", "wait",
        "send", "request", "connect", "open", "step", "watch",
        "enter", "use", "unlock",
    };

    /// <summary>
    /// Tags for a cmdlet name. Takes everything before the first dash as the
    /// verb (so <c>ConvertTo-Json</c> matches <c>ConvertTo</c>).
    /// </summary>
    public static IReadOnlyList<string> DeriveTags(string cmdletName)
    {
        if (string.IsNullOrWhiteSpace(cmdletName)) return [];
        var dash = cmdletName.IndexOf('-');
        if (dash <= 0) return [];
        var verb = cmdletName[..dash];

        if (Safe.Contains(verb))    return ["safe"];
        if (Destroy.Contains(verb)) return ["destroy"];
        if (Mutate.Contains(verb))  return ["mutate"];
        if (Act.Contains(verb))     return ["act"];
        return [];
    }

    /// <summary>Canonical ordered list used by the chip row.</summary>
    public static readonly string[] AllTags = ["safe", "mutate", "destroy", "act"];
}

/// <summary>
/// Ranking for palette search. Returns a non-negative score: zero = no match,
/// higher = better match. Multi-word queries require every word to match
/// something; the final score sums per-word best-tier scores.
/// </summary>
public static class PaletteSearch
{
    public static int Score(string query, NodeTemplate t)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;

        var normalized = query.Trim().ToLowerInvariant();
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;

        var name = (string.IsNullOrEmpty(t.CmdletName) ? t.Name : t.CmdletName).ToLowerInvariant();
        var desc = t.Description.ToLowerInvariant();
        var category = t.Category.ToLowerInvariant();
        var (verb, noun) = SplitVerbNoun(name);

        int total = 0;
        foreach (var word in words)
        {
            int best = 0;
            if (name == word)                                          best = Math.Max(best, 1000);
            else if (name.StartsWith(word))                            best = Math.Max(best, 900);
            if (!string.IsNullOrEmpty(verb) && verb.StartsWith(word))  best = Math.Max(best, 820);
            if (!string.IsNullOrEmpty(noun) && noun.StartsWith(word))  best = Math.Max(best, 800);
            if (best < 500 && name.Contains(word))                     best = Math.Max(best, 500);
            if (best < 200 && desc.Contains(word))                     best = Math.Max(best, 200);
            if (best < 100 && category.Contains(word))                 best = Math.Max(best, 100);

            if (best == 0) return 0;  // any word failing to match disqualifies the candidate
            total += best;
        }
        return total;
    }

    private static (string verb, string noun) SplitVerbNoun(string cmdletName)
    {
        var dash = cmdletName.IndexOf('-');
        return dash > 0
            ? (cmdletName[..dash], cmdletName[(dash + 1)..])
            : ("", cmdletName);
    }
}
