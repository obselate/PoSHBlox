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
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Serialize the current graph state into a .pblx JSON string.
    /// </summary>
    public static string Serialize(GraphCanvasViewModel vm, DateTime? existingCreatedUtc = null)
    {
        var now = DateTime.UtcNow;

        var doc = new PblxDocument
        {
            Version = 1,
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

        // Build node DTOs
        foreach (var node in vm.Nodes)
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
                ContainerType = node.ContainerType.ToString(),
                ContainerWidth = node.ContainerWidth,
                ContainerHeight = node.ContainerHeight,
            };

            // Nesting
            if (node.ParentContainer != null && node.ParentZone != null)
            {
                dto.ParentNodeId = node.ParentContainer.Id;
                dto.ParentZoneName = node.ParentZone.Name;
            }

            // Zones
            foreach (var zone in node.Zones)
                dto.Zones.Add(new PblxZone { Name = zone.Name });

            // Ports
            foreach (var port in node.Inputs)
                dto.Inputs.Add(new PblxPort { Name = port.Name, Type = port.Type.ToString() });
            foreach (var port in node.Outputs)
                dto.Outputs.Add(new PblxPort { Name = port.Name, Type = port.Type.ToString() });

            // Parameters
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
                });
            }

            doc.Nodes.Add(dto);
        }

        // Build connection DTOs using node ID + port index
        foreach (var conn in vm.Connections)
        {
            var sourceNode = conn.Source.Owner;
            var targetNode = conn.Target.Owner;
            if (sourceNode == null || targetNode == null) continue;

            int sourceIdx = IndexOf(sourceNode.Outputs, conn.Source);
            int targetIdx = IndexOf(targetNode.Inputs, conn.Target);
            if (sourceIdx < 0 || targetIdx < 0) continue;

            doc.Connections.Add(new PblxConnection
            {
                SourceNodeId = sourceNode.Id,
                SourcePortIndex = sourceIdx,
                TargetNodeId = targetNode.Id,
                TargetPortIndex = targetIdx,
            });
        }

        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>
    /// Deserialize a .pblx JSON string and load the graph into the view model.
    /// </summary>
    public static void Deserialize(string json, GraphCanvasViewModel vm)
    {
        var doc = JsonSerializer.Deserialize<PblxDocument>(json, Options);
        if (doc == null) return;

        if (doc.Version > 1)
            Debug.WriteLine($"[ProjectSerializer] File version {doc.Version} is newer than supported (1). Proceeding with best-effort load.");

        vm.LoadFromDocument(doc);
    }

    /// <summary>
    /// Rebuild the graph from a deserialized document. Called by <see cref="GraphCanvasViewModel.LoadFromDocument"/>.
    /// </summary>
    internal static void RebuildGraph(PblxDocument doc, GraphCanvasViewModel vm)
    {
        // Clear existing state
        vm.Connections.Clear();
        vm.Nodes.Clear();
        vm.SelectNode(null);

        var nodeMap = new Dictionary<string, GraphNode>();

        // First pass: create all nodes
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
            };

            // Container type
            if (Enum.TryParse<ContainerType>(dto.ContainerType, out var ct))
                node.ContainerType = ct;

            node.ContainerWidth = dto.ContainerWidth;
            node.ContainerHeight = dto.ContainerHeight;

            // Rebuild zones
            foreach (var zoneDto in dto.Zones)
                node.Zones.Add(new ContainerZone { Name = zoneDto.Name });

            // Rebuild ports — clear defaults first
            node.Inputs.Clear();
            node.Outputs.Clear();

            foreach (var portDto in dto.Inputs)
            {
                Enum.TryParse<PortType>(portDto.Type, out var pt);
                node.Inputs.Add(new NodePort
                {
                    Name = portDto.Name,
                    Direction = PortDirection.Input,
                    Type = pt,
                    Owner = node,
                });
            }

            foreach (var portDto in dto.Outputs)
            {
                Enum.TryParse<PortType>(portDto.Type, out var pt);
                node.Outputs.Add(new NodePort
                {
                    Name = portDto.Name,
                    Direction = PortDirection.Output,
                    Type = pt,
                    Owner = node,
                });
            }

            // Rebuild parameters
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
                });
            }

            if (node.IsContainer)
                node.RecalcZoneLayout();

            nodeMap[dto.Id] = node;
            vm.Nodes.Add(node);
        }

        // Second pass: resolve parent/zone nesting
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

        // Third pass: rebuild connections
        foreach (var cDto in doc.Connections)
        {
            if (!nodeMap.TryGetValue(cDto.SourceNodeId, out var srcNode)) continue;
            if (!nodeMap.TryGetValue(cDto.TargetNodeId, out var tgtNode)) continue;
            if (cDto.SourcePortIndex >= srcNode.Outputs.Count) continue;
            if (cDto.TargetPortIndex >= tgtNode.Inputs.Count) continue;

            var sourcePort = srcNode.Outputs[cDto.SourcePortIndex];
            var targetPort = tgtNode.Inputs[cDto.TargetPortIndex];
            vm.AddConnection(sourcePort, targetPort);
        }

        // Restore view state
        vm.PanX = doc.View.PanX;
        vm.PanY = doc.View.PanY;
        vm.Zoom = doc.View.Zoom;
        vm.ClampZoom();
    }

    private static int IndexOf<T>(IEnumerable<T> collection, T item)
    {
        int i = 0;
        foreach (var element in collection)
        {
            if (ReferenceEquals(element, item)) return i;
            i++;
        }
        return -1;
    }
}
