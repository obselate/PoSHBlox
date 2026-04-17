using PoSHBlox.Models;

namespace PoSHBlox.Services;

/// <summary>
/// Single source of truth for "can these two pins be wired together?" Used
/// both by <see cref="PoSHBlox.ViewModels.GraphCanvasViewModel"/> to validate
/// at wire-commit time and by the renderer to dim incompatible pins while a
/// drag is in progress.
/// </summary>
public static class PortCompatibility
{
    /// <summary>
    /// Direction-agnostic: does some valid (source, target) ordering exist
    /// between these two pins? True iff they're on different nodes, one is
    /// Output and the other Input, kinds match (Exec↔Exec, Data↔Data),
    /// (for data) the source type flows into the target type, and (for exec)
    /// both nodes live in the same exec scope — the codegen walks each zone
    /// as an isolated scope, so cross-zone exec wires would be silently dead.
    /// Data wires can cross zones freely (PowerShell variable scoping lets
    /// outer references work from within inner blocks).
    /// </summary>
    public static bool CanConnect(NodePort a, NodePort b)
    {
        if (ReferenceEquals(a, b)) return false;
        if (a.Owner == null || b.Owner == null || a.Owner == b.Owner) return false;
        if (a.Kind != b.Kind) return false;
        if (a.Direction == b.Direction) return false;

        var source = a.Direction == PortDirection.Output ? a : b;
        var target = ReferenceEquals(source, a) ? b : a;

        if (source.Kind == PortKind.Data && !IsDataTypeCompatible(source.DataType, target.DataType))
            return false;

        if (source.Kind == PortKind.Exec && !AreExecSiblings(source.Owner, target.Owner))
            return false;

        return true;
    }

    /// <summary>
    /// Two nodes are exec-siblings when they share the same scope: both top-
    /// level (ParentContainer == null on both), or both nested in the same
    /// container's same zone. A container's own exec pins belong to the
    /// container node itself, so wiring a sibling to a container's ExecIn/Out
    /// goes through this check with the container at its own scope level —
    /// matching PowerShell's "the if-block starts after the previous statement."
    /// </summary>
    private static bool AreExecSiblings(GraphNode a, GraphNode b)
    {
        return a.ParentContainer == b.ParentContainer
            && a.ParentZone == b.ParentZone;
    }

    /// <summary>
    /// Does a data pin of type <paramref name="src"/> flow into a pin of type
    /// <paramref name="tgt"/>? <c>Any</c> and <c>Object</c> are universal sinks;
    /// <c>StringArray</c> and <c>Collection</c> are interchangeable; a single
    /// <c>String</c> or <c>Path</c> flows into a <c>StringArray</c> sink —
    /// PowerShell auto-wraps scalars into one-element arrays for <c>[string[]]</c>
    /// params. <c>String</c> and <c>Path</c> are mutually assignable since paths
    /// are strings with a domain label. Otherwise exact type match is required.
    /// </summary>
    public static bool IsDataTypeCompatible(ParamType src, ParamType tgt)
    {
        if (src == ParamType.Any || tgt == ParamType.Any) return true;
        if (tgt == ParamType.Object) return true;
        if ((src == ParamType.StringArray && tgt == ParamType.Collection) ||
            (src == ParamType.Collection && tgt == ParamType.StringArray)) return true;
        if ((src == ParamType.String || src == ParamType.Path) && tgt == ParamType.StringArray) return true;
        if ((src == ParamType.String && tgt == ParamType.Path) ||
            (src == ParamType.Path && tgt == ParamType.String)) return true;
        return src == tgt;
    }
}
