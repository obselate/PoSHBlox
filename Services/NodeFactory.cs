using System;
using System.Linq;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Central factory for creating GraphNode instances.
/// Produces V2-shaped nodes: ExecIn/ExecOut triangle pins, N typed data-input
/// pins (one per parameter paired via <see cref="NodePort.ParameterName"/>),
/// and M data-output pins (first marked <see cref="NodePort.IsPrimary"/>).
/// </summary>
public static class NodeFactory
{
    /// <summary>
    /// Create a blank node at a given position. Ships with the default V2 pin
    /// shape from <see cref="GraphNode"/>'s constructor (ExecIn, ExecOut, Out).
    /// </summary>
    public static GraphNode CreateBlank(double x, double y)
    {
        return new GraphNode
        {
            Title = "New Block",
            X = x,
            Y = y,
        };
    }

    /// <summary>
    /// Create a node from a template definition.
    /// Returns a fully configured GraphNode with ports, parameters, and identity.
    /// </summary>
    public static GraphNode CreateFromTemplate(NodeTemplate template, double x, double y)
    {
        if (template.ContainerType != ContainerType.None)
            return CreateContainer(template.ContainerType, x, y);

        if (template.Kind == NodeKind.Value)
            return CreateValueNode(template, x, y);

        var node = new GraphNode
        {
            Title = template.Name,
            Category = template.Category,
            CmdletName = template.CmdletName,
            ScriptBody = template.ScriptBody,
            X = x,
            Y = y,
            KnownParameterSets = template.KnownParameterSets.ToArray(),
            ActiveParameterSet = template.DefaultParameterSet
                ?? template.KnownParameterSets.FirstOrDefault()
                ?? "",
        };

        CopyParameters(node, template);
        ConfigurePorts(node, template);

        return node;
    }

    /// <summary>
    /// Spawn a compact value node: no exec pins, no per-param data inputs, a
    /// single typed data output. Codegen inlines <see cref="GraphNode.ValueExpression"/>
    /// at any consumer — the node itself never appears in the generated script.
    /// </summary>
    private static GraphNode CreateValueNode(NodeTemplate template, double x, double y)
    {
        var node = new GraphNode
        {
            Kind = NodeKind.Value,
            Title = template.Name,
            Category = template.Category,
            ValueExpression = template.ValueExpression,
            X = x,
            Y = y,
        };

        CopyParameters(node, template);

        // Strip the default V2 shape assembled in GraphNode's ctor; rebuild as
        // single-output compact.
        node.Inputs.Clear();
        node.Outputs.Clear();

        var outType = template.DataOutputs.Count > 0
            ? template.DataOutputs[0].Type
            : ParamType.Any;

        node.Outputs.Add(new NodePort
        {
            Name = "",
            Kind = PortKind.Data,
            Direction = PortDirection.Output,
            DataType = outType,
            IsPrimary = true,
            Owner = node,
        });

        return node;
    }

    /// <summary>
    /// Create a container node for control flow constructs.
    /// Containers get ExecIn/ExecOut only — no data outputs in v1 per the refactor
    /// spec (ForEach is the carve-out: it exposes an Item data pin for its body).
    /// </summary>
    public static GraphNode CreateContainer(ContainerType type, double x, double y)
    {
        var node = new GraphNode
        {
            ContainerType = type,
            X = x,
            Y = y,
            Category = "Control Flow",
        };

        // Containers start with ExecIn + ExecOut only. Individual configurators
        // may adjust (e.g. Label drops both, ForEach adds an Item data output).
        node.Inputs.Clear();
        node.Outputs.Clear();
        node.Inputs.Add(new NodePort
        {
            Name = "", Kind = PortKind.Exec,
            Direction = PortDirection.Input, Owner = node,
        });
        node.Outputs.Add(new NodePort
        {
            Name = "", Kind = PortKind.Exec,
            Direction = PortDirection.Output, Owner = node,
        });

        switch (type)
        {
            case ContainerType.IfElse:   ConfigureIfElse(node);   break;
            case ContainerType.ForEach:  ConfigureForEach(node);  break;
            case ContainerType.TryCatch: ConfigureTryCatch(node); break;
            case ContainerType.While:    ConfigureWhile(node);    break;
            case ContainerType.Function: ConfigureFunction(node); break;
            case ContainerType.Label:    ConfigureLabel(node);    break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), $"Unknown container type: {type}");
        }

        // After container-specific param setup, add data-input pins for each parameter.
        AddDataInputPinsForParameters(node);

        node.RecalcZoneLayout();
        return node;
    }

    // ── Container configurations ───────────────────────────────

    private static void ConfigureIfElse(GraphNode node)
    {
        node.Title = "If / Else";
        node.ContainerWidth = 620;
        node.ContainerHeight = 320;
        // Condition is a ScriptBlock literal by default, but the generated data-
        // input pin (added by AddDataInputPinsForParameters) accepts a wired
        // upstream too — wire beats literal in codegen. No default value: the
        // validator flags unset mandatory condition so the user can't forget.
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "Condition",
            Type = ParamType.ScriptBlock,
            IsMandatory = true,
            DefaultValue = "",
            Description = "Boolean expression to evaluate — e.g. $count -gt 0. Wire a node in to compute dynamically.",
            Value = "",
        }));
        node.Zones.Add(new ContainerZone { Name = "Then" });
        node.Zones.Add(new ContainerZone { Name = "Else" });
    }

    private static void ConfigureForEach(GraphNode node)
    {
        node.Title = "ForEach-Object";
        node.ContainerWidth = 420;
        node.ContainerHeight = 300;
        // Unpaired data input: the collection to iterate. Primary pipeline target
        // so upstream can collapse into `$collection | ForEach-Object { ... }`.
        node.Inputs.Add(new NodePort
        {
            Name = "Source", Kind = PortKind.Data,
            Direction = PortDirection.Input,
            DataType = ParamType.Collection,
            IsPrimaryPipelineTarget = true,
            Owner = node,
        });
        // Data output: the current iteration item. Body nodes wire this to
        // pick up each element — codegen emits $_ by default, or the named
        // IterationVariable (below) when nesting or explicit naming requires it.
        node.Outputs.Add(new NodePort
        {
            Name = "Item", Kind = PortKind.Data,
            Direction = PortDirection.Output,
            DataType = ParamType.Any,
            IsPrimary = true,
            Owner = node,
        });
        // Optional — leaving this empty keeps the idiomatic $_ for top-level
        // ForEach-Object and auto-mints a deterministic name only when nested
        // inside another ForEach. Explicitly naming it gives the user control.
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "IterationVariable",
            Type = ParamType.String,
            IsMandatory = false,
            IsConfigOnly = true,
            DefaultValue = "",
            Description = "Name for the current item (e.g. proc, file). Leave blank to use $_. Required only when nesting ForEach loops.",
            Value = "",
        }));
        node.Zones.Add(new ContainerZone { Name = "Body" });
    }

    private static void ConfigureTryCatch(GraphNode node)
    {
        node.Title = "Try / Catch / Finally";
        node.ContainerWidth = 620;
        node.ContainerHeight = 360;
        // Why ErrorAction lives on try: PowerShell's try/catch only fires on
        // TERMINATING errors. Most built-ins emit non-terminating errors by
        // default — so to actually catch them, $ErrorActionPreference inside
        // the try block must be 'Stop'. That's the common case and the default.
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "ErrorAction",
            Type = ParamType.Enum,
            DefaultValue = "Stop",
            Description = "Sets $ErrorActionPreference inside the try block. 'Stop' converts non-terminating errors so catch fires — the typical choice.",
            ValidValues = ["Stop", "Continue", "SilentlyContinue"],
            Value = "Stop",
        }));
        node.Zones.Add(new ContainerZone { Name = "Try" });
        node.Zones.Add(new ContainerZone { Name = "Catch" });
        // Finally is optional at codegen time — the zone always exists; when
        // empty the emitter skips the finally block entirely.
        node.Zones.Add(new ContainerZone { Name = "Finally" });
    }

    private static void ConfigureWhile(GraphNode node)
    {
        node.Title = "While Loop";
        node.ContainerWidth = 420;
        node.ContainerHeight = 300;
        // No default value: $true would silently produce an infinite loop;
        // empty triggers the validator's missing-mandatory warning instead.
        // Wire a boolean-producing node into the Condition pin for a dynamic
        // condition (e.g. keep looping until a retry succeeds).
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "Condition",
            Type = ParamType.ScriptBlock,
            IsMandatory = true,
            DefaultValue = "",
            Description = "Boolean expression checked each iteration — e.g. $i -lt 10. Wire a node in to compute dynamically.",
            Value = "",
        }));
        node.Zones.Add(new ContainerZone { Name = "Body" });
    }

    private static void ConfigureFunction(GraphNode node)
    {
        node.Title = "New-Function";
        node.Category = "Function";
        node.ContainerWidth = 500;
        node.ContainerHeight = 320;
        // Function nodes define a callable — they don't execute inline, so no exec pins.
        node.Inputs.Clear();
        node.Outputs.Clear();
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "FunctionName",
            Type = ParamType.String,
            IsMandatory = true,
            DefaultValue = "Invoke-MyFunction",
            Description = "PowerShell function name (use Verb-Noun convention)",
            Value = "Invoke-MyFunction",
        }));
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "ReturnType",
            Type = ParamType.String,
            DefaultValue = "",
            Description = "Output type hint (e.g. string, int, PSObject). Leave blank for none.",
            Value = "",
        }));
        node.Parameters.Add(NewParam(node, new NodeParameter
        {
            Name = "ReturnVariable",
            Type = ParamType.String,
            DefaultValue = "",
            Description = "Variable to return (e.g. result). Leave blank for no explicit return.",
            Value = "",
        }));
        node.Zones.Add(new ContainerZone { Name = "Body" });
    }

    private static void ConfigureLabel(GraphNode node)
    {
        node.Title = "Label";
        node.Category = "Annotation";
        node.ContainerWidth = 420;
        node.ContainerHeight = 260;
        node.Inputs.Clear();
        node.Outputs.Clear();
        node.Zones.Add(new ContainerZone { Name = "Content" });
    }

    // ── Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Build V2-shaped ports for a non-container template:
    ///   ExecIn? ∷ [data pins, one per parameter] ∷ ExecOut? ∷ [data outputs].
    /// Parameters must already be copied onto the node so pairing works.
    /// </summary>
    private static void ConfigurePorts(GraphNode node, NodeTemplate template)
    {
        node.Inputs.Clear();
        node.Outputs.Clear();

        if (template.HasExecIn)
            node.Inputs.Add(new NodePort
            {
                Name = "", Kind = PortKind.Exec,
                Direction = PortDirection.Input, Owner = node,
            });

        AddDataInputPinsForParameters(node, template.PrimaryPipelineParameter);

        if (template.HasExecOut)
            node.Outputs.Add(new NodePort
            {
                Name = "", Kind = PortKind.Exec,
                Direction = PortDirection.Output, Owner = node,
            });

        // Data outputs: use template's explicit list, or fall back to a single primary Any.
        var outs = template.DataOutputs.Count > 0
            ? template.DataOutputs
            : [new DataOutputDef { Name = "Out", Type = ParamType.Any, IsPrimary = true }];

        foreach (var od in outs)
            node.Outputs.Add(new NodePort
            {
                Name = od.Name, Kind = PortKind.Data,
                Direction = PortDirection.Output,
                DataType = od.Type,
                IsPrimary = od.IsPrimary,
                Owner = node,
            });

        // Guarantee exactly one primary data output if any exist.
        if (!node.DataOutputs.Any(p => p.IsPrimary))
        {
            var first = node.DataOutputs.FirstOrDefault();
            if (first != null) first.IsPrimary = true;
        }
    }

    /// <summary>
    /// Add one typed data-input pin for each non-argument parameter on the node,
    /// pairing via <see cref="NodePort.ParameterName"/>. Primary-pipeline-target
    /// is chosen by name match or any parameter with IsPipelineInput=true.
    /// </summary>
    private static void AddDataInputPinsForParameters(GraphNode node, string? primaryPipelineParam = null)
    {
        // Switches are presence-only checkbox badges on the node, not wireable
        // data inputs — adding a pin for them would let users wire a value into
        // a parameter that doesn't take one, and codegen would emit bare -Name
        // regardless of the wired expression. Skip pin generation entirely.
        foreach (var p in node.Parameters.Where(p => !p.IsArgument && !p.IsConfigOnly && !p.IsSwitch))
        {
            bool isPrimary = (primaryPipelineParam != null
                              && string.Equals(p.Name, primaryPipelineParam, StringComparison.OrdinalIgnoreCase))
                             || p.IsPipelineInput;
            node.Inputs.Add(new NodePort
            {
                Name = p.Name,
                Kind = PortKind.Data,
                Direction = PortDirection.Input,
                DataType = p.Type,
                ParameterName = p.Name,
                IsPrimaryPipelineTarget = isPrimary,
                Owner = node,
            });
        }
    }

    private static void CopyParameters(GraphNode node, NodeTemplate template)
    {
        foreach (var pdef in template.Parameters)
        {
            node.Parameters.Add(NewParam(node, new NodeParameter
            {
                Name = pdef.Name,
                Type = pdef.Type,
                IsMandatory = pdef.IsMandatory,
                DefaultValue = pdef.DefaultValue,
                Description = pdef.Description,
                ValidValues = pdef.ValidValues,
                Value = pdef.DefaultValue,
                IsPipelineInput = pdef.IsPipelineInput,
                IsSwitch = pdef.IsSwitch,
                ParameterSets = pdef.ParameterSets.ToArray(),
                MandatoryInSets = pdef.MandatoryInSets.ToArray(),
                SupportedEditions = pdef.SupportedEditions.ToArray(),
            }));
        }
    }

    /// <summary>Stamp a parameter's <see cref="NodeParameter.Owner"/> before adding it to a node.</summary>
    private static NodeParameter NewParam(GraphNode owner, NodeParameter p)
    {
        p.Owner = owner;
        return p;
    }
}
