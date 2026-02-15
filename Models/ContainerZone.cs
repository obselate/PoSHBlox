using System.Collections.ObjectModel;

namespace PoSHBlox.Models;

/// <summary>
/// A named drop-zone inside a container where child nodes live.
/// Layout offsets are relative to the parent container's top-left corner.
/// </summary>
public class ContainerZone
{
    public string Name { get; set; } = "";

    public ObservableCollection<GraphNode> Children { get; } = new();

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// Compute absolute rectangle in canvas space given the parent container's position.
    /// </summary>
    public (double X, double Y, double W, double H) GetAbsoluteRect(GraphNode parent)
        => (parent.X + OffsetX, parent.Y + OffsetY, Width, Height);

    /// <summary>
    /// Calculate the bounding box of all children relative to the zone origin.
    /// Returns the minimum width/height the zone needs to contain all children with padding.
    /// </summary>
    public (double MinWidth, double MinHeight) GetContentBounds(GraphNode parent)
    {
        if (Children.Count == 0) return (0, 0);

        var (zx, zy, _, _) = GetAbsoluteRect(parent);
        double pad = GraphNode.ZonePadding;
        double maxRight = 0, maxBottom = 0;

        foreach (var child in Children)
        {
            double relRight = (child.X - zx) + child.EffectiveWidth + pad;
            double relBottom = (child.Y - zy) + child.Height + pad;
            if (relRight > maxRight) maxRight = relRight;
            if (relBottom > maxBottom) maxBottom = relBottom;
        }
        return (maxRight, maxBottom);
    }
}
