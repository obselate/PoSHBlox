using System;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Central factory for creating GraphNode instances.
/// All node creation goes through here so new types only need changes in one place.
/// </summary>
public static class NodeFactory
{
    /// <summary>
    /// Create a blank node at a given position.
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

        var node = new GraphNode
        {
            Title = template.Name,
            Category = template.Category,
            CmdletName = template.CmdletName,
            ScriptBody = template.ScriptBody,
            X = x,
            Y = y,
        };

        ConfigurePorts(node, template);
        CopyParameters(node, template);

        return node;
    }

    /// <summary>
    /// Create a container node for control flow constructs.
    /// To add a new container type: add the enum value, add a case here,
    /// add an emitter in ScriptGenerator, and add rendering in NodeGraphRenderer.
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

        switch (type)
        {
            case ContainerType.IfElse:
                ConfigureIfElse(node);
                break;
            case ContainerType.ForEach:
                ConfigureForEach(node);
                break;
            case ContainerType.TryCatch:
                ConfigureTryCatch(node);
                break;
            case ContainerType.While:
                ConfigureWhile(node);
                break;
            case ContainerType.Function:
                ConfigureFunction(node);
                break;
            case ContainerType.Label:
                ConfigureLabel(node);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), $"Unknown container type: {type}");
        }

        node.RecalcZoneLayout();
        return node;
    }

    // ── Container configurations ───────────────────────────────

    private static void ConfigureIfElse(GraphNode node)
    {
        node.Title = "If / Else";
        node.ContainerWidth = 620;
        node.ContainerHeight = 320;
        node.Parameters.Add(new NodeParameter
        {
            Name = "Condition",
            Type = ParamType.ScriptBlock,
            IsMandatory = true,
            DefaultValue = "$_.Status -eq 'Running'",
            Description = "PowerShell condition to evaluate",
            Value = "$_.Status -eq 'Running'",
        });
        node.Zones.Add(new ContainerZone { Name = "Then" });
        node.Zones.Add(new ContainerZone { Name = "Else" });
    }

    private static void ConfigureForEach(GraphNode node)
    {
        node.Title = "ForEach-Object";
        node.ContainerWidth = 420;
        node.ContainerHeight = 300;
        node.Parameters.Add(new NodeParameter
        {
            Name = "Variable",
            Type = ParamType.String,
            DefaultValue = "$_",
            Description = "Loop variable (default $_ for pipeline)",
            Value = "$_",
        });
        node.Zones.Add(new ContainerZone { Name = "Body" });
    }

    private static void ConfigureTryCatch(GraphNode node)
    {
        node.Title = "Try / Catch";
        node.ContainerWidth = 620;
        node.ContainerHeight = 320;
        node.Parameters.Add(new NodeParameter
        {
            Name = "ErrorAction",
            Type = ParamType.Enum,
            DefaultValue = "Stop",
            Description = "ErrorActionPreference inside try block",
            ValidValues = ["Stop", "Continue", "SilentlyContinue"],
            Value = "Stop",
        });
        node.Zones.Add(new ContainerZone { Name = "Try" });
        node.Zones.Add(new ContainerZone { Name = "Catch" });
    }

    private static void ConfigureWhile(GraphNode node)
    {
        node.Title = "While Loop";
        node.ContainerWidth = 420;
        node.ContainerHeight = 300;
        node.Parameters.Add(new NodeParameter
        {
            Name = "Condition",
            Type = ParamType.ScriptBlock,
            IsMandatory = true,
            DefaultValue = "$true",
            Description = "Loop condition",
            Value = "$true",
        });
        node.Zones.Add(new ContainerZone { Name = "Body" });
    }

    private static void ConfigureFunction(GraphNode node)
    {
        node.Title = "New-Function";
        node.Category = "Function";
        node.ContainerWidth = 500;
        node.ContainerHeight = 320;
        node.Inputs.Clear();
        node.Outputs.Clear();
        node.Parameters.Add(new NodeParameter
        {
            Name = "FunctionName",
            Type = ParamType.String,
            IsMandatory = true,
            DefaultValue = "Invoke-MyFunction",
            Description = "PowerShell function name (use Verb-Noun convention)",
            Value = "Invoke-MyFunction",
        });
        node.Parameters.Add(new NodeParameter
        {
            Name = "ReturnType",
            Type = ParamType.String,
            DefaultValue = "",
            Description = "Output type hint (e.g. string, int, PSObject). Leave blank for none.",
            Value = "",
        });
        node.Parameters.Add(new NodeParameter
        {
            Name = "ReturnVariable",
            Type = ParamType.String,
            DefaultValue = "",
            Description = "Variable to return (e.g. result). Leave blank for no explicit return.",
            Value = "",
        });
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

    private static void ConfigurePorts(GraphNode node, NodeTemplate template)
    {
        node.Inputs.Clear();
        node.Outputs.Clear();

        for (int i = 0; i < template.InputCount; i++)
        {
            var name = i < template.InputNames.Length ? template.InputNames[i] : $"In{i + 1}";
            node.Inputs.Add(new NodePort
            {
                Name = name,
                Direction = PortDirection.Input,
                Owner = node,
            });
        }

        for (int i = 0; i < template.OutputCount; i++)
        {
            var name = i < template.OutputNames.Length ? template.OutputNames[i] : $"Out{i + 1}";
            node.Outputs.Add(new NodePort
            {
                Name = name,
                Direction = PortDirection.Output,
                Owner = node,
            });
        }
    }

    private static void CopyParameters(GraphNode node, NodeTemplate template)
    {
        foreach (var pdef in template.Parameters)
        {
            node.Parameters.Add(new NodeParameter
            {
                Name = pdef.Name,
                Type = pdef.Type,
                IsMandatory = pdef.IsMandatory,
                DefaultValue = pdef.DefaultValue,
                Description = pdef.Description,
                ValidValues = pdef.ValidValues,
                Value = pdef.DefaultValue,
            });
        }
    }
}
