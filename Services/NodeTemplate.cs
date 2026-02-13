using System.Collections.Generic;
using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Describes a node that can be spawned from the palette.
/// Think of this as a blueprint — NodeFactory turns it into a real GraphNode.
/// </summary>
public class NodeTemplate
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string ScriptBody { get; set; } = "";
    public string CmdletName { get; set; } = "";
    public ContainerType ContainerType { get; set; } = ContainerType.None;
    public int InputCount { get; set; } = 1;
    public int OutputCount { get; set; } = 1;
    public string[] InputNames { get; set; } = ["In"];
    public string[] OutputNames { get; set; } = ["Out"];
    public List<ParameterDef> Parameters { get; set; } = [];
}

/// <summary>
/// Parameter definition within a template. Gets cloned into NodeParameter on spawn.
/// </summary>
public class ParameterDef
{
    public string Name { get; set; } = "";
    public ParamType Type { get; set; } = ParamType.String;
    public bool IsMandatory { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] ValidValues { get; set; } = [];
}
