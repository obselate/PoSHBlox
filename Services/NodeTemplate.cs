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

    /// <summary>V2: node has an ExecIn triangle pin. Default true.</summary>
    public bool HasExecIn { get; set; } = true;

    /// <summary>V2: node has an ExecOut triangle pin. Terminal nodes may set false.</summary>
    public bool HasExecOut { get; set; } = true;

    /// <summary>
    /// V2: name of the parameter whose paired data-input pin should be marked
    /// <see cref="NodePort.IsPrimaryPipelineTarget"/>. Enables the pipeline-collapse pass
    /// to emit <c>A | B</c> instead of <c>$x = A; B -Param $x</c>. Leave null to infer
    /// from parameters with <see cref="ParameterDef.IsPipelineInput"/> = true.
    /// </summary>
    public string? PrimaryPipelineParameter { get; set; }

    /// <summary>V2: data output pins. Empty = factory creates one primary "Out" of type Any.</summary>
    public List<DataOutputDef> DataOutputs { get; set; } = [];

    /// <summary>
    /// Palette tags derived from the PowerShell verb at load time
    /// (see <see cref="PaletteTaxonomy"/>). "safe", "mutate", "destroy", "act".
    /// Empty for non-cmdlet templates (containers, script bodies).
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// V2: parameter set metadata for cmdlets that declare multiple sets
    /// (e.g. <c>Get-ChildItem</c> has <c>Items</c>/<c>LiteralItems</c>).
    /// Empty = legacy / single-set cmdlet; no per-set filtering happens.
    /// </summary>
    public List<string> KnownParameterSets { get; set; } = [];

    /// <summary>V2: which set a node spawns with. Null = first of KnownParameterSets (if any).</summary>
    public string? DefaultParameterSet { get; set; }

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

    /// <summary>V2: true = <c>[Parameter(ValueFromPipeline)]</c>. Primary-pipeline-target candidate.</summary>
    public bool IsPipelineInput { get; set; }

    /// <summary>
    /// V2: parameter sets this param belongs to. Empty = "all sets" (common params
    /// appearing outside any set, or legacy templates without set info).
    /// </summary>
    public List<string> ParameterSets { get; set; } = [];

    /// <summary>V2: sets in which this param is mandatory. Falls back to IsMandatory when empty.</summary>
    public List<string> MandatoryInSets { get; set; } = [];
}

/// <summary>V2: describes a single data-output pin on a node.</summary>
public class DataOutputDef
{
    public string Name { get; set; } = "Out";
    public ParamType Type { get; set; } = ParamType.Any;
    public bool IsPrimary { get; set; }
}
