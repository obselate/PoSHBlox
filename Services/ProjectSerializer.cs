using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoSHBlox.Models;
using PoSHBlox.ViewModels;

namespace PoSHBlox.Services;

/// <summary>
/// Serializes and deserializes the .pblx project format.
///
/// V2 writes pin IDs on ports and connections. V1 files are migrated in-place
/// on load: ports are reshaped to V2 (ExecIn + per-parameter data inputs +
/// ExecOut + data outputs), and each V1 index-based wire becomes one exec
/// wire plus, where meaningful, one primary-pipeline-target data wire.
/// </summary>
public static class ProjectSerializer
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = PblxJsonContext.Default,
    };

    /// <summary>
    /// Serialize the current graph state into a .pblx JSON string.
    /// </summary>
    public static string Serialize(GraphCanvasViewModel vm, DateTime? existingCreatedUtc = null)
    {
        var now = DateTime.UtcNow;

        var doc = new PblxDocument
        {
            Version = CurrentVersion,
            Metadata = new PblxMetadata
            {
                CreatedUtc = existingCreatedUtc ?? now,
                ModifiedUtc = now,
            },
            View = new PblxViewState
            {
                PanX = vm.PanX,
                PanY = vm.PanY,
                Zoom = vm.Zoom,
            },
        };

        foreach (var node in vm.Nodes)
            doc.Nodes.Add(NodeToDto(node));

        foreach (var conn in vm.Connections)
        {
            var sourceNode = conn.Source.Owner;
            var targetNode = conn.Target.Owner;
            if (sourceNode == null || targetNode == null) continue;

            doc.Connections.Add(new PblxConnection
            {
                SourceNodeId = sourceNode.Id,
                SourcePortId = conn.Source.Id,
                TargetNodeId = targetNode.Id,
                TargetPortId = conn.Target.Id,
            });
        }

        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>
    /// Build a PblxNode DTO from a live <see cref="GraphNode"/>. Internal so
    /// the clipboard serializer can reuse the same schema without forking it.
    /// </summary>
    internal static PblxNode NodeToDto(GraphNode node)
    {
        var dto = new PblxNode
        {
            Id = node.Id,
            Title = node.Title,
            Category = node.Category,
            ScriptBody = node.ScriptBody,
            CmdletName = node.CmdletName,
            OutputVariable = node.OutputVariable,
            X = node.X,
            Y = node.Y,
            Width = node.Width,
            IsCollapsed = node.IsCollapsed,
            KnownParameterSets = node.KnownParameterSets,
            ActiveParameterSet = node.ActiveParameterSet,
            ContainerType = node.ContainerType.ToString(),
            ContainerWidth = node.ContainerWidth,
            ContainerHeight = node.ContainerHeight,
        };

        if (node.ParentContainer != null && node.ParentZone != null)
        {
            dto.ParentNodeId = node.ParentContainer.Id;
            dto.ParentZoneName = node.ParentZone.Name;
        }

        foreach (var zone in node.Zones)
            dto.Zones.Add(new PblxZone { Name = zone.Name });

        foreach (var port in node.Inputs)  dto.Inputs.Add(PortToDto(port));
        foreach (var port in node.Outputs) dto.Outputs.Add(PortToDto(port));

        foreach (var p in node.Parameters)
        {
            dto.Parameters.Add(new PblxParameter
            {
                Name = p.Name,
                Type = p.Type.ToString(),
                IsMandatory = p.IsMandatory,
                DefaultValue = p.DefaultValue,
                Description = p.Description,
                ValidValues = p.ValidValues,
                Value = p.Value,
                IsArgument = p.IsArgument,
                IsPipelineInput = p.IsPipelineInput,
                ParameterSets = p.ParameterSets,
                MandatoryInSets = p.MandatoryInSets,
            });
        }

        return dto;
    }

    internal static PblxPort PortToDto(NodePort p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Kind = p.Kind.ToString(),
        DataType = p.DataType.ToString(),
        ParameterName = p.ParameterName,
        IsPrimary = p.IsPrimary,
        IsPrimaryPipelineTarget = p.IsPrimaryPipelineTarget,
    };

    /// <summary>
    /// Deserialize a .pblx JSON string and load the graph into the view model.
    /// </summary>
    public static void Deserialize(string json, GraphCanvasViewModel vm)
    {
        var doc = JsonSerializer.Deserialize<PblxDocument>(json, Options);
        if (doc == null) return;

        if (doc.Version < CurrentVersion)
        {
            Debug.WriteLine($"[ProjectSerializer] Migrating v{doc.Version} → v{CurrentVersion}.");
            V1Migrator.Migrate(doc);
        }
        else if (doc.Version > CurrentVersion)
        {
            Debug.WriteLine($"[ProjectSerializer] File version {doc.Version} is newer than supported ({CurrentVersion}). Best-effort load.");
        }

        vm.LoadFromDocument(doc);
    }

    /// <summary>
    /// Rebuild the graph from a deserialized (and migrated-if-needed) document.
    /// Assumes the document is at <see cref="CurrentVersion"/>.
    /// </summary>
    internal static void RebuildGraph(PblxDocument doc, GraphCanvasViewModel vm)
    {
        vm.Connections.Clear();
        vm.Nodes.Clear();
        vm.SelectNode(null);

        var nodeMap = new Dictionary<string, GraphNode>();
        // pin-ID → NodePort, for connection resolution.
        var portMap = new Dictionary<string, NodePort>();

        // First pass: create nodes.
        foreach (var dto in doc.Nodes)
        {
            var node = new GraphNode
            {
                Id = dto.Id,
                Title = dto.Title,
                Category = dto.Category,
                ScriptBody = dto.ScriptBody,
                CmdletName = dto.CmdletName,
                OutputVariable = dto.OutputVariable,
                X = dto.X,
                Y = dto.Y,
                Width = dto.Width,
                IsCollapsed = dto.IsCollapsed,
                KnownParameterSets = dto.KnownParameterSets,
                ActiveParameterSet = dto.ActiveParameterSet,
            };

            if (Enum.TryParse<ContainerType>(dto.ContainerType, out var ct))
                node.ContainerType = ct;

            node.ContainerWidth = dto.ContainerWidth;
            node.ContainerHeight = dto.ContainerHeight;

            foreach (var zoneDto in dto.Zones)
                node.Zones.Add(new ContainerZone { Name = zoneDto.Name });

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
                    ParameterSets = pDto.ParameterSets,
                    MandatoryInSets = pDto.MandatoryInSets,
                    Owner = node,
                });
            }

            if (node.IsContainer)
                node.RecalcZoneLayout();

            nodeMap[dto.Id] = node;
            vm.Nodes.Add(node);
        }

        // Second pass: nesting.
        foreach (var dto in doc.Nodes)
        {
            if (dto.ParentNodeId == null || dto.ParentZoneName == null) continue;
            if (!nodeMap.TryGetValue(dto.Id, out var child)) continue;
            if (!nodeMap.TryGetValue(dto.ParentNodeId, out var parent)) continue;

            var zone = parent.Zones.FirstOrDefault(z => z.Name == dto.ParentZoneName);
            if (zone == null) continue;

            child.ParentContainer = parent;
            child.ParentZone = zone;
            zone.Children.Add(child);
        }

        // Third pass: connections, keyed by pin IDs.
        foreach (var cDto in doc.Connections)
        {
            if (!portMap.TryGetValue(cDto.SourcePortId, out var srcPort)) continue;
            if (!portMap.TryGetValue(cDto.TargetPortId, out var tgtPort)) continue;
            vm.AddConnection(srcPort, tgtPort);
        }

        vm.PanX = doc.View.PanX;
        vm.PanY = doc.View.PanY;
        vm.Zoom = doc.View.Zoom;
        vm.ClampZoom();
    }

    /// <summary>
    /// Build a live <see cref="NodePort"/> from its DTO with an explicit id.
    /// Shared with <see cref="ClipboardSerializer"/>, which mints fresh ids for
    /// paste; project load preserves the disk id (or mints when missing for
    /// very old files that didn't serialize port ids).
    /// </summary>
    internal static NodePort PortFromDto(GraphNode node, PblxPort dto, PortDirection dir, string id)
    {
        Enum.TryParse<PortKind>(dto.Kind, ignoreCase: true, out var kind);
        Enum.TryParse<ParamType>(dto.DataType, ignoreCase: true, out var dtype);

        return new NodePort
        {
            Id = id,
            Name = dto.Name,
            Direction = dir,
            Kind = kind,
            DataType = dtype,
            ParameterName = dto.ParameterName,
            IsPrimary = dto.IsPrimary,
            IsPrimaryPipelineTarget = dto.IsPrimaryPipelineTarget,
            Owner = node,
        };
    }

    private static void AddPortFromDto(
        GraphNode node,
        System.Collections.ObjectModel.ObservableCollection<NodePort> list,
        PblxPort dto,
        PortDirection dir,
        Dictionary<string, NodePort> portMap)
    {
        var id = string.IsNullOrEmpty(dto.Id) ? IdMint.ShortGuid() : dto.Id;
        var port = PortFromDto(node, dto, dir, id);
        list.Add(port);
        portMap[port.Id] = port;
    }
}
