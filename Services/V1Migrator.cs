using System;
using System.Collections.Generic;
using System.Linq;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// One-shot V1 → V2 migration for <see cref="PblxDocument"/>. Transforms the
/// document in place:
///   - Rebuilds each node's ports into V2 shape (ExecIn + per-parameter data
///     inputs + ExecOut + data outputs).
///   - Rewrites each V1 index-based connection into one exec wire (always)
///     plus one primary-pipeline-target data wire (when the target has a
///     pipeline input pin).
///
/// Migration is coarse by design — the spec trades precision for a one-shot
/// silent upgrade. Shipped samples are resaved manually afterwards to refine
/// per-parameter data wires.
/// </summary>
internal static class V1Migrator
{
    public static void Migrate(PblxDocument doc)
    {
        // Per-node bookkeeping: we need to remember each V1 port's original index
        // so we can map old connections to the V2 pin IDs.
        var execInByNode  = new Dictionary<string, string>();
        var execOutByNode = new Dictionary<string, string>();
        var primaryPipelineTargetByNode = new Dictionary<string, string>();
        var primaryDataOutputByNode     = new Dictionary<string, string>();

        foreach (var node in doc.Nodes)
        {
            // Preserve the original V1 port shape so we know whether the node had
            // an input/output pipeline port before we replace them with V2 shape.
            bool hadV1Input  = node.Inputs.Count  > 0;
            bool hadV1Output = node.Outputs.Count > 0;

            // Preserve original V1 names — we may reuse the first output's name
            // for the primary data output.
            string? v1FirstOutputName = hadV1Output ? node.Outputs[0].Name : null;

            node.Inputs.Clear();
            node.Outputs.Clear();

            var containerType = ParseContainer(node.ContainerType);
            bool isFunction = containerType == ContainerType.Function;
            bool isLabel    = containerType == ContainerType.Label;
            bool isForEach  = containerType == ContainerType.ForEach;

            // Function & Label: no exec pins in V2.
            bool wantExecIn  = !isFunction && !isLabel && hadV1Input;
            bool wantExecOut = !isFunction && !isLabel && hadV1Output;

            if (wantExecIn)
            {
                var id = NewId();
                node.Inputs.Add(new PblxPort
                {
                    Id = id,
                    Name = "",
                    Kind = nameof(PortKind.Exec),
                    DataType = nameof(ParamType.Any),
                });
                execInByNode[node.Id] = id;
            }

            // Per-parameter data input pins (skip arguments — those are function params).
            foreach (var p in node.Parameters.Where(p => !p.IsArgument))
            {
                bool isPipelineTarget = p.IsPipelineInput;
                var id = NewId();
                node.Inputs.Add(new PblxPort
                {
                    Id = id,
                    Name = p.Name,
                    Kind = nameof(PortKind.Data),
                    DataType = p.Type,
                    ParameterName = p.Name,
                    IsPrimaryPipelineTarget = isPipelineTarget,
                });
                if (isPipelineTarget)
                    primaryPipelineTargetByNode[node.Id] = id;
            }

            // ForEach carve-out: explicit Source pipeline-target + Item data output.
            if (isForEach)
            {
                if (!primaryPipelineTargetByNode.ContainsKey(node.Id))
                {
                    var id = NewId();
                    node.Inputs.Add(new PblxPort
                    {
                        Id = id,
                        Name = "Source",
                        Kind = nameof(PortKind.Data),
                        DataType = nameof(ParamType.Collection),
                        IsPrimaryPipelineTarget = true,
                    });
                    primaryPipelineTargetByNode[node.Id] = id;
                }

                // ForEach has no ExecOut in V2 (execution is inside its body).
                wantExecOut = true; // Actually still does — next statement after loop.
            }

            if (wantExecOut)
            {
                var id = NewId();
                node.Outputs.Add(new PblxPort
                {
                    Id = id,
                    Name = "",
                    Kind = nameof(PortKind.Exec),
                    DataType = nameof(ParamType.Any),
                });
                execOutByNode[node.Id] = id;
            }

            // Primary data output — reuse V1's first output name if present.
            if (hadV1Output && !isFunction && !isLabel)
            {
                var id = NewId();
                node.Outputs.Add(new PblxPort
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(v1FirstOutputName) ? "Out" : v1FirstOutputName,
                    Kind = nameof(PortKind.Data),
                    DataType = nameof(ParamType.Any),
                    IsPrimary = true,
                });
                primaryDataOutputByNode[node.Id] = id;
            }

            // ForEach Item output.
            if (isForEach)
            {
                var id = NewId();
                node.Outputs.Add(new PblxPort
                {
                    Id = id,
                    Name = "Item",
                    Kind = nameof(PortKind.Data),
                    DataType = nameof(ParamType.Any),
                    IsPrimary = true,
                });
                primaryDataOutputByNode[node.Id] = id;
            }
        }

        // Rewrite connections. Each V1 connection → exec wire (when both ends
        // have exec pins) + primary data wire (when target has a pipeline target).
        var newConns = new List<PblxConnection>();
        foreach (var c in doc.Connections)
        {
            if (execOutByNode.TryGetValue(c.SourceNodeId, out var execSrc)
             && execInByNode.TryGetValue(c.TargetNodeId, out var execTgt))
            {
                newConns.Add(new PblxConnection
                {
                    SourceNodeId = c.SourceNodeId,
                    SourcePortId = execSrc,
                    TargetNodeId = c.TargetNodeId,
                    TargetPortId = execTgt,
                });
            }

            if (primaryDataOutputByNode.TryGetValue(c.SourceNodeId, out var dataSrc)
             && primaryPipelineTargetByNode.TryGetValue(c.TargetNodeId, out var dataTgt))
            {
                newConns.Add(new PblxConnection
                {
                    SourceNodeId = c.SourceNodeId,
                    SourcePortId = dataSrc,
                    TargetNodeId = c.TargetNodeId,
                    TargetPortId = dataTgt,
                });
            }

            // If neither side had matching V2 pins (e.g. Function containers whose
            // V1 shape had no exec), the connection is dropped — user rewires
            // manually after the refactor.
        }

        doc.Connections = newConns;
        // Set to V2 explicitly; the V2→V3 migrator runs next when needed.
        doc.Version = 2;
    }

    private static ContainerType ParseContainer(string s)
        => Enum.TryParse<ContainerType>(s, out var ct) ? ct : ContainerType.None;

    private static string NewId() => IdMint.ShortGuid();
}
