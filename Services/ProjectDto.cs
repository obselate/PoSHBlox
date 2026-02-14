using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoSHBlox.Services;

/// <summary>
/// Flat, JSON-friendly DTOs for the .pblx project format.
/// Every DTO carries <see cref="JsonExtensionData"/> so unknown fields from
/// newer versions are preserved on round-trip (forward-compatibility).
/// </summary>

public class PblxDocument
{
    public int Version { get; set; } = 1;
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
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Pipeline";

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
    public int SourcePortIndex { get; set; }
    public string TargetNodeId { get; set; } = "";
    public int TargetPortIndex { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
