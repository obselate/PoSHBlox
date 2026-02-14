using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Generates PowerShell scripts from a node graph.
///
/// Key behaviors:
///   - Linear chains collapse into a single pipeline
///   - Function containers emit as named PowerShell functions
///   - Variables are introduced at branching points AND container inputs
///   - Container inputs flow through to zone children automatically
///   - Control flow containers emit their PowerShell equivalents
///
/// Container input flow:
///   When a container (if/else, foreach, etc.) has upstream input wired to its
///   In port, the upstream result is captured as a variable. That variable is
///   then piped into the first node of each zone's chain that has no other
///   incoming connection. This is how data crosses the container boundary.
/// </summary>
public class ScriptGenerator
{
    private readonly ObservableCollection<GraphNode> _nodes;
    private readonly ObservableCollection<NodeConnection> _connections;

    // Track variable assignments globally so nested scopes can reference parent vars
    private readonly Dictionary<string, string> _globalVarMap = new();

    public ScriptGenerator(
        ObservableCollection<GraphNode> nodes,
        ObservableCollection<NodeConnection> connections)
    {
        _nodes = nodes;
        _connections = connections;
    }

    // ── Public entry point ─────────────────────────────────────

    public string Generate()
    {
        _globalVarMap.Clear();

        var sb = new StringBuilder();
        sb.AppendLine("# ===========================================");
        sb.AppendLine("# Auto-generated PowerShell 5.1 Script");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# ===========================================");
        sb.AppendLine();

        var topLevel = _nodes.Where(n => n.ParentContainer == null).ToList();

        // Phase 1: Emit function definitions
        var functions = topLevel.Where(n => n.ContainerType == ContainerType.Function).ToList();
        if (functions.Count > 0)
        {
            sb.AppendLine("# ── Function Definitions ────────────────────");
            sb.AppendLine();
            foreach (var fn in functions)
                EmitFunctionDefinition(sb, fn);
        }

        // Phase 2: Emit top-level execution
        var sorted = TopologicalSort(topLevel);
        if (sorted == null)
        {
            sb.AppendLine("# ERROR: Cycle detected in graph!");
            return sb.ToString();
        }

        if (sorted.Count > 0)
        {
            sb.AppendLine("# ── Execution ───────────────────────────────");
            sb.AppendLine();
            EmitScope(sb, sorted, indent: 0, inputVar: null);
        }

        return sb.ToString();
    }

    // ── Scope emission ─────────────────────────────────────────

    /// <summary>
    /// Emit a set of topologically-sorted nodes within a scope.
    /// inputVar: if non-null, this variable is available as implicit input
    /// for any head node (no incoming connections) in this scope.
    /// </summary>
    private void EmitScope(StringBuilder sb, List<GraphNode> sorted, int indent, string? inputVar)
    {
        var pad = Indent(indent);
        var scopeIds = new HashSet<string>(sorted.Select(n => n.Id));

        var chains = BuildChains(sorted, scopeIds);
        var emitted = new HashSet<string>();
        var localVarMap = new Dictionary<string, string>();

        // First pass: figure out which chains/nodes need variables
        // A chain needs a variable if:
        //   1. Its last node feeds multiple downstream consumers, OR
        //   2. Its last node feeds a control flow container (container needs a named ref)
        foreach (var chain in chains)
        {
            var lastNode = chain[^1];
            bool needsVar = CountDownstream(lastNode, scopeIds) > 1
                         || HasContainerDownstream(lastNode, scopeIds);

            if (needsVar)
            {
                string varName = MakeVariableName(chain);
                foreach (var n in chain)
                {
                    localVarMap[n.Id] = varName;
                    _globalVarMap[n.Id] = varName;
                }
            }
        }

        // Also assign variables for standalone nodes feeding containers
        foreach (var node in sorted.Where(n => !n.IsContainer || n.ContainerType == ContainerType.Function))
        {
            if (localVarMap.ContainsKey(node.Id)) continue;
            if (HasContainerDownstream(node, scopeIds))
            {
                string varName = MakeVariableNameForNode(node);
                localVarMap[node.Id] = varName;
                _globalVarMap[node.Id] = varName;
            }
        }

        // Build a lookup: node ID → chain it belongs to
        var chainByNodeId = new Dictionary<string, List<GraphNode>>();
        foreach (var chain in chains)
            foreach (var n in chain)
                chainByNodeId[n.Id] = chain;

        // Second pass: emit in topological order (chains and containers interleaved)
        // This ensures nodes connected to a container's output are emitted AFTER
        // the container, not before.
        foreach (var node in sorted)
        {
            if (emitted.Contains(node.Id)) continue;

            if (node.IsContainer && node.ContainerType != ContainerType.Function)
            {
                emitted.Add(node.Id);
                string? containerInput = ResolveContainerInput(node, scopeIds, localVarMap, inputVar);
                EmitContainer(sb, node, indent, containerInput);
                sb.AppendLine();
            }
            else if (chainByNodeId.TryGetValue(node.Id, out var chain))
            {
                if (chain.Any(n => emitted.Contains(n.Id))) continue;
                foreach (var n in chain) emitted.Add(n.Id);

                var firstNode = chain[0];
                var lastNode = chain[^1];

                // Resolve upstream: check local var map, global var map, or implicit input
                string? upstreamExpr = ResolveUpstream(firstNode, scopeIds, localVarMap)
                                    ?? (CountIncoming(firstNode, scopeIds) == 0 ? inputVar : null);

                string pipeline = BuildPipelineExpression(chain, upstreamExpr);

                if (localVarMap.TryGetValue(lastNode.Id, out var assignVar))
                    sb.AppendLine($"{pad}${assignVar} = {pipeline}");
                else
                    sb.AppendLine($"{pad}{pipeline}");

                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Resolve what input expression a container should receive.
    /// Checks: upstream variable → upstream in global map → scope's implicit input.
    /// </summary>
    private string? ResolveContainerInput(GraphNode container, HashSet<string> scopeIds,
        Dictionary<string, string> localVarMap, string? scopeInputVar)
    {
        var upstream = GetUpstreamNode(container, scopeIds);
        if (upstream != null)
        {
            // Check local scope first, then global
            if (localVarMap.TryGetValue(upstream.Id, out var localVar))
                return "$" + localVar;
            if (_globalVarMap.TryGetValue(upstream.Id, out var globalVar))
                return "$" + globalVar;
        }

        // If no explicit upstream, inherit scope's implicit input
        if (upstream == null && CountIncoming(container, scopeIds) == 0)
            return scopeInputVar;

        return null;
    }

    private string? ResolveUpstream(GraphNode node, HashSet<string> scopeIds,
        Dictionary<string, string> localVarMap)
    {
        var upstream = GetUpstreamNode(node, scopeIds);
        if (upstream == null) return null;

        if (localVarMap.TryGetValue(upstream.Id, out var localVar))
            return "$" + localVar;
        if (_globalVarMap.TryGetValue(upstream.Id, out var globalVar))
            return "$" + globalVar;

        return null;
    }

    // ── Chain detection ────────────────────────────────────────

    private List<List<GraphNode>> BuildChains(List<GraphNode> sorted, HashSet<string> scopeIds)
    {
        var chains = new List<List<GraphNode>>();
        var assigned = new HashSet<string>();

        foreach (var node in sorted)
        {
            if (assigned.Contains(node.Id)) continue;
            if (node.IsContainer && node.ContainerType != ContainerType.Function) continue;

            int inCount = CountIncoming(node, scopeIds);
            if (inCount > 0)
            {
                var upstream = GetUpstreamNode(node, scopeIds);
                if (upstream != null && CountDownstream(upstream, scopeIds) == 1
                    && !IsControlFlowContainer(upstream))
                    continue;
            }

            var chain = new List<GraphNode> { node };
            assigned.Add(node.Id);

            var current = node;
            while (true)
            {
                var next = GetSingleDownstreamNode(current, scopeIds);
                if (next == null) break;
                if (assigned.Contains(next.Id)) break;
                if (IsControlFlowContainer(next)) break;
                if (CountIncoming(next, scopeIds) != 1) break;

                chain.Add(next);
                assigned.Add(next.Id);
                current = next;
            }

            chains.Add(chain);
        }

        return chains;
    }

    // ── Pipeline expression building ───────────────────────────

    private string BuildPipelineExpression(List<GraphNode> chain, string? upstreamExpr)
    {
        var segments = new List<string>();

        if (!string.IsNullOrEmpty(upstreamExpr))
            segments.Add(upstreamExpr);

        foreach (var node in chain)
        {
            if (node.ContainerType == ContainerType.Function)
            {
                var fnName = GetParamValue(node, "FunctionName", "Invoke-MyFunction");
                segments.Add(fnName);
            }
            else
            {
                segments.Add(BuildNodeExpression(node));
            }
        }

        return string.Join(" | ", segments);
    }

    private static string BuildNodeExpression(GraphNode node)
    {
        if (node.IsCmdletNode)
        {
            var args = FormatArgs(node);
            return string.IsNullOrEmpty(args) ? node.CmdletName : $"{node.CmdletName} {args}";
        }

        return node.ScriptBody.Trim();
    }

    // ── Function container emission ────────────────────────────

    private void EmitFunctionDefinition(StringBuilder sb, GraphNode fnNode)
    {
        var fnName = GetParamValue(fnNode, "FunctionName", "Invoke-MyFunction");
        var inputParam = GetParamValue(fnNode, "InputParam", "");
        var bodyZone = FindZone(fnNode, "Body");

        sb.AppendLine($"function {fnName} {{");

        if (!string.IsNullOrWhiteSpace(inputParam))
        {
            sb.AppendLine($"    param(");
            sb.AppendLine($"        [Parameter(ValueFromPipeline)]");
            sb.AppendLine($"        ${inputParam}");
            sb.AppendLine($"    )");
            sb.AppendLine($"    process {{");
            EmitZoneAsScope(sb, bodyZone, indent: 2, inputVar: "$" + inputParam);
            sb.AppendLine($"    }}");
        }
        else
        {
            EmitZoneAsScope(sb, bodyZone, indent: 1, inputVar: null);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ── Control flow container emission ────────────────────────

    /// <summary>
    /// containerInput: the expression referencing the data wired into this container's
    /// In port. Gets passed to zone children as their implicit input.
    /// </summary>
    private void EmitContainer(StringBuilder sb, GraphNode container, int indent, string? containerInput)
    {
        var pad = Indent(indent);

        switch (container.ContainerType)
        {
            case ContainerType.IfElse:
                EmitIfElse(sb, container, pad, indent, containerInput);
                break;
            case ContainerType.ForEach:
                EmitForEach(sb, container, pad, indent, containerInput);
                break;
            case ContainerType.TryCatch:
                EmitTryCatch(sb, container, pad, indent, containerInput);
                break;
            case ContainerType.While:
                EmitWhile(sb, container, pad, indent, containerInput);
                break;
            case ContainerType.Function:
                break; // emitted in Phase 1
            default:
                sb.AppendLine($"{pad}# WARNING: Unknown container type '{container.ContainerType}'");
                break;
        }
    }

    private void EmitIfElse(StringBuilder sb, GraphNode container, string pad, int indent, string? containerInput)
    {
        var condition = GetParamValue(container, "Condition", "$true");
        var thenZone = FindZone(container, "Then");
        var elseZone = FindZone(container, "Else");

        sb.AppendLine($"{pad}if ({condition}) {{");
        EmitZoneAsScope(sb, thenZone, indent + 1, containerInput);
        sb.AppendLine($"{pad}}}");

        if (elseZone != null && elseZone.Children.Count > 0)
        {
            sb.AppendLine($"{pad}else {{");
            EmitZoneAsScope(sb, elseZone, indent + 1, containerInput);
            sb.AppendLine($"{pad}}}");
        }
    }

    private void EmitForEach(StringBuilder sb, GraphNode container, string pad, int indent, string? containerInput)
    {
        var bodyZone = FindZone(container, "Body");

        if (!string.IsNullOrEmpty(containerInput))
            sb.AppendLine($"{pad}{containerInput} | ForEach-Object {{");
        else
            sb.AppendLine($"{pad}ForEach-Object {{");

        EmitZoneAsScope(sb, bodyZone, indent + 1, "$_");
        sb.AppendLine($"{pad}}}");
    }

    private void EmitTryCatch(StringBuilder sb, GraphNode container, string pad, int indent, string? containerInput)
    {
        var tryZone = FindZone(container, "Try");
        var catchZone = FindZone(container, "Catch");

        sb.AppendLine($"{pad}try {{");
        EmitZoneAsScope(sb, tryZone, indent + 1, containerInput);
        sb.AppendLine($"{pad}}}");

        sb.AppendLine($"{pad}catch {{");
        EmitZoneAsScope(sb, catchZone, indent + 1, null);
        sb.AppendLine($"{pad}}}");
    }

    private void EmitWhile(StringBuilder sb, GraphNode container, string pad, int indent, string? containerInput)
    {
        var condition = GetParamValue(container, "Condition", "$true");
        var bodyZone = FindZone(container, "Body");

        sb.AppendLine($"{pad}while ({condition}) {{");
        EmitZoneAsScope(sb, bodyZone, indent + 1, containerInput);
        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// Emit a zone's children with the full chain-collapsing logic.
    /// inputVar flows through as implicit input for head nodes.
    /// </summary>
    private void EmitZoneAsScope(StringBuilder sb, ContainerZone? zone, int indent, string? inputVar)
    {
        if (zone == null || zone.Children.Count == 0)
        {
            sb.AppendLine($"{Indent(indent)}# (empty)");
            return;
        }

        var sorted = TopologicalSort(zone.Children.ToList());
        if (sorted == null)
        {
            sb.AppendLine($"{Indent(indent)}# ERROR: Cycle detected in zone '{zone.Name}'!");
            return;
        }

        EmitScope(sb, sorted, indent, inputVar);
    }

    // ── Graph analysis helpers ─────────────────────────────────

    private int CountDownstream(GraphNode node, HashSet<string> scopeIds)
        => _connections.Count(c =>
            c.Source.Owner == node && scopeIds.Contains(c.Target.Owner!.Id));

    private int CountIncoming(GraphNode node, HashSet<string> scopeIds)
        => _connections.Count(c =>
            c.Target.Owner == node && scopeIds.Contains(c.Source.Owner!.Id));

    /// <summary>
    /// Does this node feed into any control flow container in the scope?
    /// If so, it needs a variable (containers reference input by variable, not pipeline).
    /// </summary>
    private bool HasContainerDownstream(GraphNode node, HashSet<string> scopeIds)
        => _connections.Any(c =>
            c.Source.Owner == node
            && scopeIds.Contains(c.Target.Owner!.Id)
            && IsControlFlowContainer(c.Target.Owner!));

    private GraphNode? GetSingleDownstreamNode(GraphNode node, HashSet<string> scopeIds)
    {
        var downstream = _connections
            .Where(c => c.Source.Owner == node && scopeIds.Contains(c.Target.Owner!.Id))
            .Select(c => c.Target.Owner!)
            .Distinct()
            .ToList();
        return downstream.Count == 1 ? downstream[0] : null;
    }

    private GraphNode? GetUpstreamNode(GraphNode node, HashSet<string> scopeIds)
    {
        var upstream = _connections
            .Where(c => c.Target.Owner == node && scopeIds.Contains(c.Source.Owner!.Id))
            .Select(c => c.Source.Owner!)
            .Distinct()
            .ToList();
        return upstream.Count == 1 ? upstream[0] : null;
    }

    // ── Topological sort ───────────────────────────────────────

    private List<GraphNode>? TopologicalSort(List<GraphNode> nodes)
    {
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));
        var result = new List<GraphNode>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        bool Visit(GraphNode node)
        {
            if (visiting.Contains(node.Id)) return false;
            if (visited.Contains(node.Id)) return true;
            visiting.Add(node.Id);

            foreach (var dep in _connections
                .Where(c => c.Target.Owner == node && nodeIds.Contains(c.Source.Owner!.Id))
                .Select(c => c.Source.Owner!)
                .Distinct())
            {
                if (!Visit(dep)) return false;
            }

            visiting.Remove(node.Id);
            visited.Add(node.Id);
            result.Add(node);
            return true;
        }

        foreach (var node in nodes)
            if (!Visit(node)) return null;

        return result;
    }

    // ── Naming helpers ─────────────────────────────────────────

    /// <summary>
    /// Get the variable name for a chain. Uses the first node's identity.
    /// </summary>
    private static string MakeVariableName(List<GraphNode> chain)
        => MakeVariableNameForNode(chain[0]);

    /// <summary>
    /// Get the variable name for a node.
    /// Priority: user-set OutputVariable → auto-generated Title_ShortId.
    /// The short ID suffix guarantees uniqueness even with duplicate titles.
    /// </summary>
    private static string MakeVariableNameForNode(GraphNode node)
    {
        // If user explicitly named the output variable, use it as-is
        if (!string.IsNullOrWhiteSpace(node.OutputVariable))
            return SanitizeVariableName(node.OutputVariable);

        // Auto-generate: Title_ShortId (first 4 chars of the node's unique ID)
        var baseName = node.ContainerType == ContainerType.Function
            ? node.Parameters.FirstOrDefault(p => p.Name == "FunctionName")?.EffectiveValue ?? "Function"
            : node.Title;

        return SanitizeVariableName(baseName) + "_" + node.Id[..4];
    }

    private static string SanitizeVariableName(string name)
    {
        var parts = name.Split('-', ' ', '.', '_');
        var result = string.Join("",
            parts.Select(p =>
                p.Length > 0 ? char.ToUpper(p[0]) + p[1..] : ""));

        result = Regex.Replace(result, @"[^a-zA-Z0-9]", "");
        return string.IsNullOrEmpty(result) ? "Result" : result;
    }

    // ── Utility helpers ────────────────────────────────────────

    private static bool IsControlFlowContainer(GraphNode node)
        => node.IsContainer && node.ContainerType != ContainerType.Function;

    private static string FormatArgs(GraphNode node)
        => string.Join(" ",
            node.Parameters
                .Select(p => p.ToPowerShellArg())
                .Where(a => !string.IsNullOrEmpty(a)));

    private static string GetParamValue(GraphNode node, string paramName, string fallback)
        => node.Parameters.FirstOrDefault(p => p.Name == paramName)?.EffectiveValue ?? fallback;

    private static ContainerZone? FindZone(GraphNode container, string name)
        => container.Zones.FirstOrDefault(z => z.Name == name);

    private static string Indent(int level) => new(' ', level * 4);
}
