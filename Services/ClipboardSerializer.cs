using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Serializes a subset of the graph (selected nodes + the wires whose
/// endpoints are both inside the selection) into a JSON envelope suitable for
/// the system clipboard. Cross-process: a payload copied from one PoSHBlox
/// window can be pasted into another since it rides on the OS text clipboard.
///
/// Reuses the project-file DTOs (<see cref="PblxNode"/>, <see cref="PblxConnection"/>)
/// so there's no parallel schema to maintain. Paste mints fresh node + port
/// IDs so the same clipboard can be pasted multiple times into the same graph
/// without ID collisions, and remaps the connection refs accordingly.
/// </summary>
public static class ClipboardSerializer
{
    /// <summary>
    /// Magic string baked into the envelope so paste can cheaply reject
    /// arbitrary text on the clipboard before invoking the JSON parser.
    /// </summary>
    public const string MagicString = "pblx-clip";
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = PblxJsonContext.Default,
    };

    public sealed class Payload
    {
        public string Magic { get; set; } = MagicString;
        public int Version { get; set; } = CurrentVersion;
        public List<PblxNode> Nodes { get; set; } = [];
        public List<PblxConnection> Connections { get; set; } = [];
    }

    /// <summary>
    /// Serialize a selection into a JSON envelope. Wires are restricted to
    /// those whose source AND target nodes are both in <paramref name="selection"/>;
    /// nesting references to nodes outside the selection are dropped (paste
    /// without nesting is a clean V1 — the user can re-attach by dragging into
    /// a zone).
    /// </summary>
    public static string SerializeSelection(
        IEnumerable<GraphNode> selection,
        IEnumerable<NodeConnection> connections)
    {
        var nodes = selection.ToList();
        if (nodes.Count == 0) return "";

        var ids = new HashSet<string>(nodes.Select(n => n.Id));
        var payload = new Payload();

        foreach (var node in nodes)
        {
            var dto = ProjectSerializer.NodeToDto(node);
            // Drop parent nesting if the parent isn't part of the payload —
            // otherwise paste would dangle on a phantom container ID.
            if (dto.ParentNodeId != null && !ids.Contains(dto.ParentNodeId))
            {
                dto.ParentNodeId = null;
                dto.ParentZoneName = null;
            }
            payload.Nodes.Add(dto);
        }

        foreach (var conn in connections)
        {
            var src = conn.Source.Owner;
            var tgt = conn.Target.Owner;
            if (src == null || tgt == null) continue;
            if (!ids.Contains(src.Id) || !ids.Contains(tgt.Id)) continue;
            payload.Connections.Add(new PblxConnection
            {
                SourceNodeId = src.Id,
                SourcePortId = conn.Source.Id,
                TargetNodeId = tgt.Id,
                TargetPortId = conn.Target.Id,
            });
        }

        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>
    /// Best-effort parse. Returns null when the text isn't ours, so callers
    /// can silently no-op on a Ctrl+V over arbitrary clipboard contents.
    /// </summary>
    public static Payload? TryDeserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!text.Contains(MagicString, StringComparison.Ordinal)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<Payload>(text, Options);
            if (p == null || p.Magic != MagicString) return null;
            return p;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Materialize a payload into ready-to-add <see cref="GraphNode"/>s with
    /// fresh node + port IDs and positions translated so the bounding box's
    /// top-left lands at <paramref name="pasteX"/>,<paramref name="pasteY"/>.
    /// Returns the new nodes and the (already remapped) port pairs the caller
    /// should hand to <c>vm.AddConnection</c>.
    /// </summary>
    public static (List<GraphNode> Nodes, List<(NodePort Source, NodePort Target)> Wires) Build(
        Payload payload, double pasteX, double pasteY)
    {
        var portMap = new Dictionary<string, NodePort>();
        var nodeMap = new Dictionary<string, GraphNode>();

        // Compute the source bounding box (top-left) so the whole cluster
        // translates as one unit — multi-node copies preserve relative layout.
        double minX = payload.Nodes.Count > 0 ? payload.Nodes.Min(n => n.X) : 0;
        double minY = payload.Nodes.Count > 0 ? payload.Nodes.Min(n => n.Y) : 0;
        double dx = pasteX - minX;
        double dy = pasteY - minY;

        var newNodes = new List<GraphNode>(payload.Nodes.Count);
        foreach (var dto in payload.Nodes)
        {
            var node = new GraphNode
            {
                // init-only Id ⇒ must be set in the initializer; mint fresh
                // so paste-into-same-graph doesn't collide with originals.
                Id = IdMint.ShortGuid(),
                Title = dto.Title,
                Category = dto.Category,
                ScriptBody = dto.ScriptBody,
                CmdletName = dto.CmdletName,
                // Reset OutputVariable — pasted nodes get a fresh auto-name.
                OutputVariable = "",
                X = dto.X + dx,
                Y = dto.Y + dy,
                Width = dto.Width,
                IsCollapsed = dto.IsCollapsed,
                KnownParameterSets = (string[])dto.KnownParameterSets.Clone(),
                ActiveParameterSet = dto.ActiveParameterSet,
            };

            if (Enum.TryParse<ContainerType>(dto.ContainerType, out var ct))
                node.ContainerType = ct;
            node.ContainerWidth = dto.ContainerWidth;
            node.ContainerHeight = dto.ContainerHeight;

            foreach (var zoneDto in dto.Zones)
                node.Zones.Add(new ContainerZone { Name = zoneDto.Name });

            // Default constructor seeds V2 ports — replace with the payload's
            // exact pin shape so per-parameter pairing + custom outputs survive.
            node.Inputs.Clear();
            node.Outputs.Clear();
            foreach (var portDto in dto.Inputs)
                AddPortFromDto(node, node.Inputs, portDto, PortDirection.Input, portMap);
            foreach (var portDto in dto.Outputs)
                AddPortFromDto(node, node.Outputs, portDto, PortDirection.Output, portMap);

            foreach (var pDto in dto.Parameters)
            {
                Enum.TryParse<ParamType>(pDto.Type, out var paramType);
                node.Parameters.Add(new NodeParameter
                {
                    Name = pDto.Name,
                    Type = paramType,
                    IsMandatory = pDto.IsMandatory,
                    DefaultValue = pDto.DefaultValue,
                    Description = pDto.Description,
                    ValidValues = pDto.ValidValues,
                    Value = pDto.Value,
                    IsArgument = pDto.IsArgument,
                    IsPipelineInput = pDto.IsPipelineInput,
                    ParameterSets = (string[])pDto.ParameterSets.Clone(),
                    MandatoryInSets = (string[])pDto.MandatoryInSets.Clone(),
                    Owner = node,
                });
            }

            if (node.IsContainer)
                node.RecalcZoneLayout();

            nodeMap[dto.Id] = node;
            newNodes.Add(node);
        }

        // Remap connections via the per-paste port map (old payload IDs →
        // freshly-minted runtime ports). Connections with a missing endpoint
        // are silently dropped; the source they reference must have been
        // outside the selection.
        var wires = new List<(NodePort, NodePort)>();
        foreach (var cDto in payload.Connections)
        {
            if (!portMap.TryGetValue(cDto.SourcePortId, out var src)) continue;
            if (!portMap.TryGetValue(cDto.TargetPortId, out var tgt)) continue;
            wires.Add((src, tgt));
        }

        return (newNodes, wires);
    }

    private static void AddPortFromDto(
        GraphNode node,
        ObservableCollection<NodePort> list,
        PblxPort dto,
        PortDirection dir,
        Dictionary<string, NodePort> portMap)
    {
        // Mint a fresh id so paste-into-same-graph doesn't collide with the
        // originals; the map is keyed by the OLD dto id so connection refs
        // can be remapped after all nodes are built.
        var port = ProjectSerializer.PortFromDto(node, dto, dir, IdMint.ShortGuid());
        list.Add(port);
        portMap[dto.Id] = port;
    }
}
