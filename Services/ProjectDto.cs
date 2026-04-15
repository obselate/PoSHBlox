using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoSHBlox.Services;

/// <summary>
/// Flat, JSON-friendly DTOs for the .pblx project format.
/// Every DTO carries <see cref="JsonExtensionData"/> so unknown fields from
/// newer versions are preserved on round-trip (forward-compatibility).
///
/// Version 2: pin IDs replace port indices, ports carry Kind/DataType/etc.
/// Version 1 documents are migrated in-place on load by ProjectSerializer.
/// </summary>

public class PblxDocument
{
    public int Version { get; set; } = 2;
    public PblxMetadata Metadata { get; set; } = new();
    public PblxViewState View { get; set; } = new();
    public List<PblxNode> Nodes { get; set; } = [];
    public List<PblxConnection> Connections { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxMetadata
{
    public string? Name { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxViewState
{
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double Zoom { get; set; } = 1.0;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxNode
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string ScriptBody { get; set; } = "";
    public string CmdletName { get; set; } = "";
    public string OutputVariable { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 200;

    // Container fields (only populated for containers)
    public string ContainerType { get; set; } = "None";
    public double ContainerWidth { get; set; } = 500;
    public double ContainerHeight { get; set; } = 300;
    public List<PblxZone> Zones { get; set; } = [];

    // Nesting
    public string? ParentNodeId { get; set; }
    public string? ParentZoneName { get; set; }

    // Ports & params
    public List<PblxPort> Inputs { get; set; } = [];
    public List<PblxPort> Outputs { get; set; } = [];
    public List<PblxParameter> Parameters { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxPort
{
    /// <summary>Stable pin identity — required in V2, absent in V1 (migration mints one).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>V2: <c>Exec</c> or <c>Data</c>. Default Data for backwards JSON shape.</summary>
    public string Kind { get; set; } = "Data";

    /// <summary>V2: the <see cref="PoSHBlox.Models.ParamType"/> carried by this data pin.</summary>
    public string DataType { get; set; } = "Any";

    /// <summary>V2: for data inputs derived from a parameter, the parameter name.</summary>
    public string? ParameterName { get; set; }

    /// <summary>V2: output-side flag — the primary data producer (for pipeline collapse).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>V2: input-side flag — accepts upstream via pipeline (ValueFromPipeline).</summary>
    public bool IsPrimaryPipelineTarget { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "String";
    public bool IsMandatory { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] ValidValues { get; set; } = [];
    public string Value { get; set; } = "";
    public bool IsArgument { get; set; }
    public bool IsPipelineInput { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxZone
{
    public string Name { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public class PblxConnection
{
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";

    /// <summary>Pin IDs survive reordering / template updates.</summary>
    public string SourcePortId { get; set; } = "";
    public string TargetPortId { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
