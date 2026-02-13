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
}
