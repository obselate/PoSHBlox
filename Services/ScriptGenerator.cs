using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// PowerShell code generator. Drives emission from an explicit exec-wire
/// traversal. Data references resolve through the graph: wired input pins
/// produce <c>-Param $upstreamVar</c>, unwired input pins produce the
/// parameter's literal value.
///
/// Pipeline collapse: when node A has <c>A.ExecOut → B.ExecIn</c> AND
/// <c>A.PrimaryDataOutput → B.PrimaryPipelineTarget</c> AND A's primary-out has
/// exactly one consumer AND A has no user-set OutputVariable, emit <c>A | B</c>
/// instead of <c>$a = A; B -InputObject $a</c>. Chains extend greedily while
/// each link still satisfies those conditions.
///
/// ForEach.Item: a data wire from a ForEach's "Item" output compiles to <c>$_</c>
/// inside its body; no variable is introduced for the iteration item.
/// </summary>
public class ScriptGenerator
{
    private readonly ObservableCollection<GraphNode> _nodes;
    private readonly ObservableCollection<NodeConnection> _connections;

    /// <summary>node-id → assigned variable name (no $ prefix).</summary>
    private readonly Dictionary<string, string> _varMap = new();

    /// <summary>
    /// ForEach node-id → name bound inside the body (no $ prefix), or null
    /// to signal "emit the raw <c>$_</c>". Populated by <see cref="EmitForEach"/>
    /// before its body walks so <see cref="ResolveUpstreamRef"/> can substitute
    /// the correct token when a body node wires the Item pin.
    /// </summary>
    private readonly Dictionary<string, string?> _foreachVarMap = new();

    /// <summary>node-ids already emitted; guards converged-exec paths from double-emission.</summary>
    private readonly HashSet<string> _emitted = new();

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
        _varMap.Clear();
        _emitted.Clear();
        _foreachVarMap.Clear();

        var sb = new StringBuilder();
        sb.AppendLine("# ===========================================");
        sb.AppendLine("# Auto-generated PowerShell Script");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# ===========================================");
        sb.AppendLine();

        var topLevel = _nodes.Where(n => n.ParentContainer == null).ToList();

        // Phase 1: function definitions.
        var functions = topLevel.Where(n => n.ContainerType == ContainerType.Function).ToList();
        if (functions.Count > 0)
        {
            sb.AppendLine("# ── Function Definitions ────────────────────");
            sb.AppendLine();
            foreach (var fn in functions)
            {
                EmitFunctionDefinition(sb, fn);
                _emitted.Add(fn.Id);
            }
        }

        // Phase 2: top-level exec walk (excluding function definitions).
        var execables = topLevel
            .Where(n => n.ContainerType != ContainerType.Function)
            .ToList();

        if (execables.Count > 0)
        {
            sb.AppendLine("# ── Execution ───────────────────────────────");
            sb.AppendLine();
            EmitExecScope(sb, execables, indent: 0);
        }

        return sb.ToString();
    }

    // ── Scope walk ─────────────────────────────────────────────

    /// <summary>
    /// Walk a scope (a zone's children or the top-level set) starting from every
    /// exec root — a node whose ExecIn has no in-scope incoming connection, or
    /// that simply has no ExecIn pin (e.g. Get-Process with HasExecIn=false).
    /// Emission is chain-aware: each emission may consume one or more nodes.
    /// </summary>
    private void EmitExecScope(StringBuilder sb, List<GraphNode> scope, int indent)
    {
        // Value nodes never generate statements — they only exist as inline
        // references from the consumer. Exclude them from the exec walk so
        // the "unreachable helper" pass below doesn't accidentally emit them.
        var execScope = scope.Where(n => !n.IsValueNode).ToList();
        var scopeIds = execScope.Select(n => n.Id).ToHashSet();

        // Roots: ordered to match document order for stable output.
        var roots = execScope.Where(n => IsExecRoot(n, scopeIds)).ToList();

        foreach (var root in roots)
            WalkExec(sb, root, scopeIds, indent);

        // Catch any nodes unreachable via exec (e.g. disconnected helpers).
        // Emit them as standalone statements so the user sees them in output.
        foreach (var n in execScope)
        {
            if (_emitted.Contains(n.Id)) continue;
            WalkExec(sb, n, scopeIds, indent);
        }
    }

    /// <summary>
    /// Starting at <paramref name="start"/>, greedily form a pipeline-collapsable
    /// chain, emit it, then recurse on the exec successor of the last node.
    /// Returns when the exec-successor is already emitted or leaves the scope.
    /// </summary>
    private void WalkExec(StringBuilder sb, GraphNode start, HashSet<string> scopeIds, int indent)
    {
        var current = start;
        while (current != null && !_emitted.Contains(current.Id) && scopeIds.Contains(current.Id))
        {
            if (current.IsContainer)
            {
                EmitContainer(sb, current, indent);
                _emitted.Add(current.Id);
            }
            else
            {
                var chain = BuildPipelineChain(current, scopeIds);
                EmitChain(sb, chain, indent);
                foreach (var c in chain) _emitted.Add(c.Id);
                current = chain[^1];
            }

            current = ExecSuccessor(current, scopeIds);
        }
    }

    // ── Pipeline chain construction ────────────────────────────

    /// <summary>
    /// Extend a chain from <paramref name="start"/> while each successive link
    /// satisfies the collapse rule: exec→ lines up with primary-out→pipeline-target,
    /// source has a single consumer of its primary-out, and no user OutputVariable.
    /// </summary>
    private List<GraphNode> BuildPipelineChain(GraphNode start, HashSet<string> scopeIds)
    {
        var chain = new List<GraphNode> { start };
        var current = start;

        while (true)
        {
            var next = ExecSuccessor(current, scopeIds);
            if (next == null || next.IsContainer || _emitted.Contains(next.Id)) break;
            if (!CanCollapseInto(current, next, scopeIds)) break;
            chain.Add(next);
            current = next;
        }

        return chain;
    }

    /// <summary>
    /// Can <paramref name="b"/> receive <paramref name="a"/>'s primary output via
    /// a single <c>|</c> rather than an explicit <c>$var</c> reference?
    /// </summary>
    private bool CanCollapseInto(GraphNode a, GraphNode b, HashSet<string> scopeIds)
    {
        if (!string.IsNullOrWhiteSpace(a.OutputVariable)) return false;

        var primaryOut = a.PrimaryDataOutput;
        var pipelineTarget = b.PrimaryPipelineTarget;
        if (primaryOut == null || pipelineTarget == null) return false;

        // a.primary-out must connect to b's primary pipeline target, and a.primary-out
        // must have exactly one consumer (only b).
        int outConsumers = _connections.Count(c =>
            c.Source == primaryOut && scopeIds.Contains(c.Target.Owner!.Id));
        if (outConsumers != 1) return false;

        return _connections.Any(c => c.Source == primaryOut && c.Target == pipelineTarget);
    }

    // ── Emit a (possibly 1-long) pipeline chain ────────────────

    private void EmitChain(StringBuilder sb, List<GraphNode> chain, int indent)
    {
        var pad = Indent(indent);

        // Pre-assign variable for every node in the chain so downstream nodes in
        // the same scope can reference them. Pipeline-collapsed chains share the
        // chain's single variable: downstream references to any chain member
        // resolve to the last node's var.
        string tailVar = MakeVariableNameForNode(chain[^1]);
        foreach (var n in chain) _varMap[n.Id] = tailVar;

        // Chain expression. Each node may emit a splat hashtable into the
        // preamble StringBuilder first; the inline part joins with `|`.
        var preamble = new StringBuilder();
        var parts = new List<string>();
        for (int i = 0; i < chain.Count; i++)
        {
            bool collapseInput = i > 0; // everyone but the head consumes | upstream
            parts.Add(BuildNodeExpression(chain[i], collapseInput, pad, preamble));
        }
        string pipeline = string.Join(" | ", parts);

        if (preamble.Length > 0)
            sb.Append(preamble);

        if (NeedsAssignment(chain[^1]))
            sb.AppendLine($"{pad}${tailVar} = {pipeline}");
        else
            sb.AppendLine($"{pad}{pipeline}");
        sb.AppendLine();
    }

    /// <summary>
    /// Number of inline args at which a node switches from
    /// <c>Cmd -A x -B y -C z -D w</c> to a splat hashtable plus <c>Cmd @args</c>.
    /// Chosen as 4 so that nodes with up to three set parameters stay compact,
    /// while anything larger becomes dramatically more readable.
    /// </summary>
    private const int SplatThreshold = 4;

    /// <summary>
    /// Build <c>Cmdlet -Param value -Other $upstream</c> (or bare ScriptBody for
    /// non-cmdlet nodes). <paramref name="collapseInput"/> suppresses the primary
    /// pipeline-target parameter — it's consumed by the preceding <c>|</c>.
    /// When the node would emit <see cref="SplatThreshold"/> or more inline
    /// args, the params are written to <paramref name="preamble"/> as a splat
    /// hashtable and the returned inline becomes <c>Cmd @argsVar</c>.
    /// </summary>
    private string BuildNodeExpression(GraphNode node, bool collapseInput, string pad, StringBuilder preamble)
    {
        if (!node.IsCmdletNode)
            return node.ScriptBody.Trim();

        // Collect each arg as both its inline form ("-Name value") and its
        // splat entry ("Name = value") so we can choose presentation after
        // counting.
        var entries = new List<(string Inline, string Splat)>();

        foreach (var param in node.Parameters.Where(p => !p.IsArgument && IsParamInActiveSet(node, p)))
        {
            var pin = node.Inputs.FirstOrDefault(p => p.ParameterName == param.Name);
            if (pin == null)
            {
                // Legacy / unpaired parameter — emit literal if present.
                var lit = param.ToPowerShellArg();
                if (string.IsNullOrEmpty(lit)) continue;
                var splat = param.ToSplatEntry();
                entries.Add((lit, splat));
                continue;
            }

            if (collapseInput && pin.IsPrimaryPipelineTarget)
                continue; // consumed by | upstream

            var upstream = FirstConnectionTargeting(pin);
            if (upstream != null)
            {
                var refExpr = ResolveUpstreamRef(upstream.Source);
                entries.Add(($"-{param.Name} {refExpr}", $"{param.Name} = {refExpr}"));
            }
            else
            {
                var lit = param.ToPowerShellArg();
                if (string.IsNullOrEmpty(lit)) continue;
                var splat = param.ToSplatEntry();
                entries.Add((lit, splat));
            }
        }

        if (entries.Count == 0) return node.CmdletName;

        // Below threshold: stay inline for compactness.
        if (entries.Count < SplatThreshold)
            return $"{node.CmdletName} {string.Join(" ", entries.Select(e => e.Inline))}";

        // At / above threshold: emit a splat hashtable into the preamble and
        // reference it inline. Var name derived from cmdlet + node short-id
        // so multiple invocations of the same cmdlet don't collide.
        var splatVar = MakeSplatVarName(node);
        preamble.AppendLine($"{pad}${splatVar} = @{{");
        foreach (var (_, splat) in entries)
            preamble.AppendLine($"{pad}    {splat}");
        preamble.AppendLine($"{pad}}}");
        return $"{node.CmdletName} @{splatVar}";
    }

    /// <summary>
    /// Build a splat variable name from a cmdlet's name and the node's short
    /// ID, e.g. <c>copyItemArgs_ab12</c>. Camel-cased so it reads naturally
    /// beside hand-written PowerShell.
    /// </summary>
    private static string MakeSplatVarName(GraphNode node)
    {
        var raw = string.IsNullOrEmpty(node.CmdletName) ? node.Title : node.CmdletName;
        // Strip non-alphanumerics, lowercase the first letter.
        var cleaned = Regex.Replace(raw, @"[^a-zA-Z0-9]", "");
        if (cleaned.Length == 0) cleaned = "Args";
        var camelled = char.ToLowerInvariant(cleaned[0]) + cleaned[1..];
        return $"{camelled}Args_{node.Id[..4]}";
    }

    /// <summary>
    /// Resolve a wire's source pin to a PowerShell expression:
    ///   - ForEach.Item → <c>$_</c> by default; when the ForEach has an explicit
    ///     IterationVariable or is nested inside another ForEach, the item is
    ///     rebound to a named variable (see <see cref="_foreachVarMap"/>) to
    ///     keep the outer pin from shadowing.
    ///   - anything else → $ of the source node's assigned variable.
    /// </summary>
    private string ResolveUpstreamRef(NodePort sourcePort)
    {
        var owner = sourcePort.Owner!;

        // Value nodes: emit the literal expression directly. No variable is
        // introduced — the node itself never appears in the generated script.
        if (owner.IsValueNode)
            return owner.ResolvedValueExpression;

        if (owner.ContainerType == ContainerType.ForEach && sourcePort.Name == "Item")
        {
            if (_foreachVarMap.TryGetValue(owner.Id, out var named) && named != null)
                return "$" + named;
            return "$_";
        }

        if (_varMap.TryGetValue(owner.Id, out var name))
            return "$" + name;

        // Upstream hasn't been emitted yet (cycle or out-of-scope). Fall back to
        // a predicted name so the script still parses — flag with a comment in Phase N.
        var predicted = MakeVariableNameForNode(owner);
        _varMap[owner.Id] = predicted;
        return "$" + predicted;
    }

    /// <summary>Does this node need a <c>$var = ...</c> assignment?</summary>
    private bool NeedsAssignment(GraphNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.OutputVariable)) return true;

        // If any data output has a downstream consumer, we need the var.
        foreach (var outPin in node.DataOutputs)
        {
            if (_connections.Any(c => c.Source == outPin)) return true;
        }
        return false;
    }

    // ── Container emission ─────────────────────────────────────

    private void EmitContainer(StringBuilder sb, GraphNode container, int indent)
    {
        var pad = Indent(indent);

        switch (container.ContainerType)
        {
            case ContainerType.IfElse:   EmitIfElse(sb, container, pad, indent);   break;
            case ContainerType.ForEach:  EmitForEach(sb, container, pad, indent);  break;
            case ContainerType.TryCatch: EmitTryCatch(sb, container, pad, indent); break;
            case ContainerType.While:    EmitWhile(sb, container, pad, indent);    break;
            case ContainerType.Label:    EmitLabel(sb, container, indent);         break;
            case ContainerType.Function: EmitFunctionDefinition(sb, container);    break;
            default:
                sb.AppendLine($"{pad}# WARNING: Unknown container type '{container.ContainerType}'");
                break;
        }
        sb.AppendLine();
    }

    private void EmitIfElse(StringBuilder sb, GraphNode c, string pad, int indent)
    {
        // Wire beats literal: if the user wired a boolean-producing node into the
        // Condition pin, use that upstream's variable ref. Otherwise use the
        // literal ScriptBlock from the param. An unset condition emits `$false`
        // plus a warning comment so the script still parses.
        var condition = ResolveParamOrWire(c, "Condition", fallback: null);
        if (string.IsNullOrWhiteSpace(condition))
        {
            sb.AppendLine($"{pad}# WARNING: If/Else condition not set — defaulting to $false.");
            condition = "$false";
        }

        sb.AppendLine($"{pad}if ({condition}) {{");
        EmitZone(sb, FindZone(c, "Then"), indent + 1);
        sb.AppendLine($"{pad}}}");

        var elseZone = FindZone(c, "Else");
        if (elseZone != null && elseZone.Children.Count > 0)
        {
            sb.AppendLine($"{pad}else {{");
            EmitZone(sb, elseZone, indent + 1);
            sb.AppendLine($"{pad}}}");
        }
    }

    private void EmitForEach(StringBuilder sb, GraphNode c, string pad, int indent)
    {
        // Collection source comes from the "Source" data input pin. Its upstream's
        // primary-out resolves to the pipeline prefix.
        var sourcePin = c.Inputs.FirstOrDefault(p => p.Name == "Source");
        string prefix = "";
        if (sourcePin != null)
        {
            var upstream = FirstConnectionTargeting(sourcePin);
            if (upstream != null)
                prefix = ResolveUpstreamRef(upstream.Source) + " | ";
        }

        // Iteration variable: let users name it explicitly so nested ForEach
        // loops don't clobber each other's $_. When empty AND the body contains
        // another ForEach, we auto-bind to a deterministic per-node name so the
        // outer's Item pin resolves unambiguously. Otherwise the idiomatic $_
        // stays as-is for the common single-level case.
        var body = FindZone(c, "Body");
        var explicitName = GetParamValue(c, "IterationVariable", "").Trim();
        string? itemVar;
        if (!string.IsNullOrEmpty(explicitName))
            itemVar = SanitizeVariableName(explicitName);
        else if (body != null && ContainsContainer(body, ContainerType.ForEach))
            itemVar = "item_" + c.Id[..4];
        else
            itemVar = null; // null ⇒ emit $_ verbatim

        _foreachVarMap[c.Id] = itemVar;

        sb.AppendLine($"{pad}{prefix}ForEach-Object {{");
        if (itemVar != null)
            sb.AppendLine($"{pad}    ${itemVar} = $_");
        EmitZone(sb, body, indent + 1);
        sb.AppendLine($"{pad}}}");
    }

    private void EmitTryCatch(StringBuilder sb, GraphNode c, string pad, int indent)
    {
        sb.AppendLine($"{pad}try {{");

        // ErrorAction only matters inside try: converts non-terminating errors
        // to terminating so catch actually fires. Empty / Continue is PS
        // default — skip the assignment in that case. Assignment leaks to the
        // enclosing scope by design (try/catch don't create scopes); users
        // who care can wrap the whole node in a Function container.
        var errorAction = ResolveParamOrWire(c, "ErrorAction", fallback: "");
        if (!string.IsNullOrWhiteSpace(errorAction)
            && !errorAction.Equals("Continue", StringComparison.OrdinalIgnoreCase))
        {
            // Literal enum value → quote it; wired upstream ref (starts with $) → pass through.
            var rhs = errorAction.StartsWith("$") ? errorAction : $"'{errorAction}'";
            sb.AppendLine($"{Indent(indent + 1)}$ErrorActionPreference = {rhs}");
        }

        EmitZone(sb, FindZone(c, "Try"), indent + 1);
        sb.AppendLine($"{pad}}}");
        sb.AppendLine($"{pad}catch {{");
        EmitZone(sb, FindZone(c, "Catch"), indent + 1);
        sb.AppendLine($"{pad}}}");

        // Finally is optional — emit only when the zone exists AND has children.
        var finallyZone = FindZone(c, "Finally");
        if (finallyZone != null && finallyZone.Children.Count > 0)
        {
            sb.AppendLine($"{pad}finally {{");
            EmitZone(sb, finallyZone, indent + 1);
            sb.AppendLine($"{pad}}}");
        }
    }

    private void EmitWhile(StringBuilder sb, GraphNode c, string pad, int indent)
    {
        // Wire beats literal; empty falls back to $false so we don't generate
        // an unintended infinite loop on an unconfigured node.
        var condition = ResolveParamOrWire(c, "Condition", fallback: null);
        if (string.IsNullOrWhiteSpace(condition))
        {
            sb.AppendLine($"{pad}# WARNING: While condition not set — defaulting to $false (loop will not run).");
            condition = "$false";
        }

        sb.AppendLine($"{pad}while ({condition}) {{");
        EmitZone(sb, FindZone(c, "Body"), indent + 1);
        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// Resolve a container parameter to the PowerShell expression that should
    /// land in the emitted snippet. If the matching data-input pin is wired,
    /// the upstream's resolved reference wins (so the user can compute the
    /// condition / error-action dynamically). Otherwise the literal param
    /// value is used, or <paramref name="fallback"/> when the literal is empty.
    /// </summary>
    private string? ResolveParamOrWire(GraphNode node, string paramName, string? fallback)
    {
        var pin = node.Inputs.FirstOrDefault(p =>
            string.Equals(p.ParameterName, paramName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));

        if (pin != null)
        {
            var upstream = FirstConnectionTargeting(pin);
            if (upstream != null)
                return ResolveUpstreamRef(upstream.Source);
        }

        var literal = node.Parameters.FirstOrDefault(p => p.Name == paramName)?.EffectiveValue;
        return string.IsNullOrWhiteSpace(literal) ? fallback : literal;
    }

    /// <summary>
    /// Recursively scan a zone's children for any container of the given type.
    /// Used by ForEach to detect nested ForEach loops so it can disambiguate
    /// the iteration variable.
    /// </summary>
    private static bool ContainsContainer(ContainerZone zone, ContainerType type)
    {
        foreach (var child in zone.Children)
        {
            if (child.ContainerType == type) return true;
            if (child.IsContainer)
                foreach (var inner in child.Zones)
                    if (ContainsContainer(inner, type)) return true;
        }
        return false;
    }

    private void EmitLabel(StringBuilder sb, GraphNode c, int indent)
    {
        // Pass-through: label zones just wrap their content. Use the first
        // zone rather than looking up by name — Label is single-zone so
        // first-zone is unambiguous, and this lets the renderer drop the
        // zone header without the codegen losing its reference.
        EmitZone(sb, c.Zones.FirstOrDefault(), indent);
    }

    private void EmitZone(StringBuilder sb, ContainerZone? zone, int indent)
    {
        if (zone == null || zone.Children.Count == 0)
        {
            sb.AppendLine($"{Indent(indent)}# (empty)");
            return;
        }
        EmitExecScope(sb, zone.Children.ToList(), indent);
    }

    // ── Function emission (V1-compatible shape, cleaned up for V2) ──

    private void EmitFunctionDefinition(StringBuilder sb, GraphNode fnNode)
    {
        var fnName = GetParamValue(fnNode, "FunctionName", "Invoke-MyFunction");
        var returnType = GetParamValue(fnNode, "ReturnType", "");
        var returnVar = GetParamValue(fnNode, "ReturnVariable", "");
        var bodyZone = FindZone(fnNode, "Body");
        var arguments = fnNode.Parameters.Where(p => p.IsArgument).ToList();

        sb.AppendLine($"function {fnName} {{");

        if (!string.IsNullOrWhiteSpace(returnType))
            sb.AppendLine($"    [OutputType([{returnType}])]");

        if (arguments.Count > 0)
        {
            sb.AppendLine("    param(");
            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var attrs = new List<string>();
                if (arg.IsMandatory) attrs.Add("Mandatory");
                if (arg.IsPipelineInput) attrs.Add("ValueFromPipeline");

                if (attrs.Count > 0)
                    sb.AppendLine($"        [Parameter({string.Join(", ", attrs)})]");

                var comma = i < arguments.Count - 1 ? "," : "";
                var defaultVal = !string.IsNullOrWhiteSpace(arg.DefaultValue) && arg.Type != ParamType.Bool
                    ? $" = \"{arg.DefaultValue}\""
                    : "";
                sb.AppendLine($"        [{arg.PowerShellTypeName}]${arg.Name}{defaultVal}{comma}");
                if (i < arguments.Count - 1) sb.AppendLine();
            }
            sb.AppendLine("    )");

            var pipelineArg = arguments.FirstOrDefault(a => a.IsPipelineInput);
            if (pipelineArg != null)
            {
                sb.AppendLine("    process {");
                EmitZone(sb, bodyZone, indent: 2);
                if (!string.IsNullOrWhiteSpace(returnVar))
                    sb.AppendLine($"        return ${returnVar}");
                sb.AppendLine("    }");
            }
            else
            {
                EmitZone(sb, bodyZone, indent: 1);
                if (!string.IsNullOrWhiteSpace(returnVar))
                    sb.AppendLine($"    return ${returnVar}");
            }
        }
        else
        {
            EmitZone(sb, bodyZone, indent: 1);
            if (!string.IsNullOrWhiteSpace(returnVar))
                sb.AppendLine($"    return ${returnVar}");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ── Exec-graph helpers ─────────────────────────────────────

    private bool IsExecRoot(GraphNode n, HashSet<string> scopeIds)
    {
        // A node with no ExecIn pin (HasExecIn=false) is always a root.
        var execIn = n.ExecInPort;
        if (execIn == null) return true;
        return !_connections.Any(c =>
            c.Target == execIn && scopeIds.Contains(c.Source.Owner!.Id));
    }

    /// <summary>Follow the single outgoing exec wire from <paramref name="n"/>'s ExecOut.</summary>
    private GraphNode? ExecSuccessor(GraphNode n, HashSet<string> scopeIds)
    {
        var execOut = n.ExecOutPort;
        if (execOut == null) return null;
        var conn = _connections.FirstOrDefault(c =>
            c.Source == execOut && scopeIds.Contains(c.Target.Owner!.Id));
        return conn?.Target.Owner;
    }

    private NodeConnection? FirstConnectionTargeting(NodePort pin)
        => _connections.FirstOrDefault(c => c.Target == pin);

    /// <summary>
    /// Is <paramref name="param"/> active for this node's currently-selected
    /// parameter set? Legacy nodes (no declared sets) and common params (no
    /// per-param set list) are always active.
    /// </summary>
    private static bool IsParamInActiveSet(GraphNode node, NodeParameter param)
    {
        if (node.KnownParameterSets.Length == 0) return true;
        if (param.ParameterSets.Length == 0) return true;
        return param.ParameterSets.Contains(node.ActiveParameterSet, StringComparer.OrdinalIgnoreCase);
    }

    // ── Naming helpers (unchanged from V1) ─────────────────────

    private static string MakeVariableNameForNode(GraphNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.OutputVariable))
            return SanitizeVariableName(node.OutputVariable);

        var baseName = node.ContainerType == ContainerType.Function
            ? node.Parameters.FirstOrDefault(p => p.Name == "FunctionName")?.EffectiveValue ?? "Function"
            : node.Title;

        return SanitizeVariableName(baseName) + "_" + node.Id[..4];
    }

    private static string SanitizeVariableName(string name)
    {
        var parts = name.Split('-', ' ', '.', '_');
        var result = string.Join("",
            parts.Select(p => p.Length > 0 ? char.ToUpper(p[0]) + p[1..] : ""));
        result = Regex.Replace(result, @"[^a-zA-Z0-9]", "");
        return string.IsNullOrEmpty(result) ? "Result" : result;
    }

    // ── Utility ────────────────────────────────────────────────

    private static string GetParamValue(GraphNode node, string paramName, string fallback)
        => node.Parameters.FirstOrDefault(p => p.Name == paramName)?.EffectiveValue ?? fallback;

    private static ContainerZone? FindZone(GraphNode container, string name)
        => container.Zones.FirstOrDefault(z => z.Name == name);

    private static string Indent(int level) => new(' ', level * 4);
}
