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
    /// Output and the other Input, kinds match (Exec↔Exec, Data↔Data), and
    /// (for data) the source type flows into the target type.
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

        return true;
    }

    /// <summary>
    /// Does a data pin of type <paramref name="src"/> flow into a pin of type
    /// <paramref name="tgt"/>? <c>Any</c> and <c>Object</c> are universal sinks;
    /// <c>StringArray</c> and <c>Collection</c> are interchangeable; otherwise
    /// exact type match is required.
    /// </summary>
    public static bool IsDataTypeCompatible(ParamType src, ParamType tgt)
    {
        if (src == ParamType.Any || tgt == ParamType.Any) return true;
        if (tgt == ParamType.Object) return true;
        if ((src == ParamType.StringArray && tgt == ParamType.Collection) ||
            (src == ParamType.Collection && tgt == ParamType.StringArray)) return true;
        return src == tgt;
    }
}
