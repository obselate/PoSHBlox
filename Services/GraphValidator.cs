using System;
using System.Collections.Generic;
using System.Linq;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Produces per-node issue lists from the current graph state. Pure function:
/// given the nodes + connections, returns a map keyed by node ID. The VM's
/// <c>RefreshValidation</c> then mirrors the output onto each node's
/// <see cref="GraphNode.Issues"/> collection so the renderer + properties
/// panel can bind to it.
///
/// Current checks (v1):
///   - Missing mandatory: a parameter flagged mandatory (honoring
///     MandatoryInSets against the node's ActiveParameterSet) has no wired
///     input and no literal value.
///   - Orphan: a non-container, non-function node with no exec predecessor
///     AND no consumer of its primary data output. Almost always a mistake.
///   - Exec cycle: a node participates in a cycle on the exec graph.
///   - Unresolved upstream: a wired input pin references a source node that
///     isn't in the graph. Shouldn't happen given normal cleanup, but a
///     cheap belt-and-braces check.
/// </summary>
public static class GraphValidator
{
    public static Dictionary<string, List<GraphIssue>> Validate(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<NodeConnection> connections)
    {
        var result = new Dictionary<string, List<GraphIssue>>();
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));

        // 1. Missing mandatory params.
        foreach (var node in nodes)
        {
            foreach (var p in node.Parameters)
            {
                if (p.IsArgument) continue;                 // function arg defs, not cmdlet params
                if (!IsMandatoryInContext(p, node)) continue;

                bool wired = p.InputPort != null
                    && connections.Any(c => c.Target == p.InputPort);
                if (wired) continue;

                if (!string.IsNullOrWhiteSpace(p.Value)) continue;

                Add(result, node.Id, new GraphIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = IssueCode.MissingMandatory,
                    Message = $"'{p.Name}' is required (no wire or value).",
                    Parameter = p,
                });
            }
        }

        // 2. Orphan nodes — no exec predecessor AND no data consumer.
        //    Functions are callables (no exec pins, ExecInPort == null so they're
        //    naturally considered "source" and skipped). Other containers with
        //    exec pins (If/ForEach/While/TryCatch/Label) follow the same rule
        //    as regular nodes: unwired means they won't run.
        foreach (var node in nodes)
        {
            bool hasExecIncoming =
                node.ExecInPort != null
                && connections.Any(c => c.Target == node.ExecInPort && nodeIds.Contains(c.Source.Owner!.Id));
            bool hasExecOutgoing =
                node.ExecOutPort != null
                && connections.Any(c => c.Source == node.ExecOutPort && nodeIds.Contains(c.Target.Owner!.Id));
            bool hasDataConsumer =
                node.DataOutputs.Any(o => connections.Any(c => c.Source == o));

            // Source nodes (HasExecIn=false) are considered naturally rooted — not orphans.
            bool isSource = node.ExecInPort == null;

            if (!isSource && !hasExecIncoming && !hasExecOutgoing && !hasDataConsumer)
            {
                Add(result, node.Id, new GraphIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = IssueCode.Orphan,
                    Message = node.IsContainer
                        ? "Container is not connected to an exec chain — its body will not run."
                        : "Node has no exec or data connections — will not execute.",
                });
            }
        }

        // 2b. Empty required zones on containers. Fresh drops produce these —
        // the amber warning tells the user what's still missing, same pattern
        // as MissingMandatory on params. Optional zones (If.Else, TryCatch's
        // Catch/Finally, Label.Content) don't trigger this.
        foreach (var node in nodes.Where(n => n.IsContainer))
        {
            foreach (var zoneName in RequiredZonesFor(node.ContainerType))
            {
                var zone = node.Zones.FirstOrDefault(z => z.Name == zoneName);
                if (zone != null && zone.Children.Count == 0)
                {
                    Add(result, node.Id, new GraphIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = IssueCode.EmptyContainerZone,
                        Message = $"'{zoneName}' zone is empty — drop at least one node into it.",
                    });
                }
            }
        }

        // 3. Exec cycles (strict SCC would be overkill — an exec-wire DFS with
        //    visiting stack catches the cases users actually create).
        var inCycle = FindExecCycles(nodes, connections);
        foreach (var id in inCycle)
        {
            Add(result, id, new GraphIssue
            {
                Severity = IssueSeverity.Error,
                Code = IssueCode.ExecCycle,
                Message = "Node is part of an exec-wire cycle.",
            });
        }

        // 4. Unresolved upstream — wired input points at a source owner not in the graph.
        foreach (var c in connections)
        {
            var srcOwner = c.Source.Owner;
            var tgtOwner = c.Target.Owner;
            if (tgtOwner == null) continue;

            if (srcOwner == null || !nodeIds.Contains(srcOwner.Id))
            {
                Add(result, tgtOwner.Id, new GraphIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = IssueCode.UnresolvedUpstream,
                    Message = $"Wire targets '{c.Target.Name}' from a source that's no longer in the graph.",
                });
            }
        }

        return result;
    }

    private static bool IsMandatoryInContext(NodeParameter p, GraphNode node)
    {
        if (p.MandatoryInSets.Length == 0) return p.IsMandatory;
        return p.MandatoryInSets.Contains(node.ActiveParameterSet, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zones that must be non-empty for the container to produce meaningful
    /// PowerShell. Optional zones (If.Else, TryCatch.Catch/Finally, Label.Content)
    /// are deliberately omitted — an empty Else is just "no else branch", which
    /// is a common and valid shape.
    /// </summary>
    private static IEnumerable<string> RequiredZonesFor(ContainerType type) => type switch
    {
        ContainerType.IfElse   => ["Then"],
        ContainerType.ForEach  => ["Body"],
        ContainerType.While    => ["Body"],
        ContainerType.TryCatch => ["Try"],
        ContainerType.Function => ["Body"],
        _                      => [],
    };

    /// <summary>
    /// Find every node participating in an exec-wire cycle. DFS with a visiting
    /// set; any back-edge marks the whole current stack as cycle members.
    /// </summary>
    private static HashSet<string> FindExecCycles(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<NodeConnection> connections)
    {
        var cycleIds = new HashSet<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var stack = new List<string>();
        var byId = nodes.ToDictionary(n => n.Id);

        // Build adjacency: node-id → list of exec successors' node-ids.
        var adj = new Dictionary<string, List<string>>();
        foreach (var n in nodes)
        {
            var outs = new List<string>();
            if (n.ExecOutPort != null)
            {
                foreach (var c in connections)
                    if (c.Source == n.ExecOutPort && c.Target.Owner != null)
                        outs.Add(c.Target.Owner.Id);
            }
            adj[n.Id] = outs;
        }

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (visiting.Contains(id))
            {
                // Found a back-edge — everything from the first hit of `id`
                // through the top of the stack is in the cycle.
                int start = stack.IndexOf(id);
                if (start >= 0)
                    for (int i = start; i < stack.Count; i++)
                        cycleIds.Add(stack[i]);
                return;
            }
            visiting.Add(id);
            stack.Add(id);
            if (adj.TryGetValue(id, out var next))
                foreach (var n in next)
                    Visit(n);
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(id);
            visited.Add(id);
        }

        foreach (var n in nodes)
            Visit(n.Id);

        return cycleIds;
    }

    private static void Add(Dictionary<string, List<GraphIssue>> map, string nodeId, GraphIssue issue)
    {
        if (!map.TryGetValue(nodeId, out var list))
            map[nodeId] = list = [];
        list.Add(issue);
    }
}
