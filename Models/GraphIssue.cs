using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Models;

/// <summary>Severity determines whether the node renders a red (error) or amber (warning) badge.</summary>
public enum IssueSeverity { Warning, Error }

/// <summary>
/// Machine-readable classifier for each issue. Keeps the UI surface and the
/// test surface decoupled from the human-readable message so we can reword
/// without breaking anything.
/// </summary>
public enum IssueCode
{
    /// <summary>A mandatory parameter is neither wired nor has a literal value.</summary>
    MissingMandatory,

    /// <summary>The node participates in an exec-wire cycle.</summary>
    ExecCycle,

    /// <summary>Node has no exec predecessor and no data consumer — unlikely to execute.</summary>
    Orphan,

    /// <summary>A wired input pin points at a source that no longer exists.</summary>
    UnresolvedUpstream,

    /// <summary>A container's required zone has no children (e.g. If with an empty Then body).</summary>
    EmptyContainerZone,
}

/// <summary>
/// A single validation finding attached to a <see cref="GraphNode"/>.
/// Produced by <see cref="PoSHBlox.Services.GraphValidator"/>, consumed by the
/// renderer (badge / border) and the properties panel (issue list).
/// </summary>
public partial class GraphIssue : ObservableObject
{
    public required IssueSeverity Severity { get; init; }
    public required IssueCode Code { get; init; }
    public required string Message { get; init; }

    /// <summary>Optional: the specific parameter the issue refers to, for UI focus.</summary>
    public NodeParameter? Parameter { get; init; }
}
