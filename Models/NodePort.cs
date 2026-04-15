using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Models;

public partial class NodePort : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public PortDirection Direction { get; set; }

    /// <summary>Exec pin (triangle) or data pin (circle).</summary>
    public PortKind Kind { get; set; } = PortKind.Data;

    /// <summary>Value type for data pins. Ignored for exec pins. <c>Any</c> matches anything.</summary>
    public ParamType DataType { get; set; } = ParamType.Any;

    /// <summary>
    /// For data input pins derived from a NodeParameter: the parameter name.
    /// Lets the properties panel hide the literal editor when this pin is wired.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Output pin flag: the "primary" data output (used for pipeline-collapse).
    /// Exactly one data output per node should set this true.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Input pin flag: the pin that accepts upstream via <c>ValueFromPipeline</c>.
    /// Required for the <c>A | B</c> pipeline-collapse pass.
    /// </summary>
    public bool IsPrimaryPipelineTarget { get; set; }

    /// <summary>Legacy V1 field — preserved for serializer migration, removed in Step 9.</summary>
    public PortType Type { get; set; } = PortType.Pipeline;

    public GraphNode? Owner { get; set; }

    [ObservableProperty] private double _anchorX;
    [ObservableProperty] private double _anchorY;
}
