using System;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using PoSHBlox.Models;

namespace PoSHBlox.Rendering;

/// <summary>
/// Computes per-node effective width based on content (title + pin labels).
/// Grows above the node's declared <see cref="GraphNode.Width"/> when labels
/// would collide; never shrinks below it. Called by both the renderer (to
/// draw the body + position pins) and the canvas (for hit-testing) so layout
/// stays consistent without the model having to know about text measurement.
/// </summary>
public static class NodeLayout
{
    /// <summary>Minimum node width regardless of content.</summary>
    public const double MinRegularNodeWidth = 200;

    /// <summary>Horizontal padding between the two pin columns inside a node body.</summary>
    private const double ColumnGutter = 40;

    /// <summary>Padding reserved at each side of the title for the header chevron.</summary>
    private const double HeaderChevronReserve = 24;

    /// <summary>Extra reserve when a validation badge sits left of the chevron.</summary>
    private const double HeaderBadgeReserve = 20;

    /// <summary>
    /// Width the renderer should use for a regular (non-container) node. Containers
    /// pass through — their width is user-resizable and handled separately.
    /// </summary>
    public static double GetEffectiveWidth(GraphNode node)
    {
        if (node.IsContainer) return node.ContainerWidth;

        // Title drives the header's minimum width: text + side padding + chevron
        // reserve + optional badge reserve when the node has validation issues.
        double titleRequired = MeasureWidth(node.Title, 13, FontWeight.Bold)
                             + 28 + HeaderChevronReserve
                             + (node.HasIssues ? HeaderBadgeReserve : 0);

        // Each body row has a left pin, a left label, a gutter, a right label, a right pin.
        // Find the widest row across inputs and outputs (aligned by row index).
        var inputs  = node.VisibleDataInputs.ToList();
        var outputs = node.DataOutputs.ToList();
        int rowCount = Math.Max(inputs.Count, outputs.Count);

        double rowRequired = 0;
        for (int i = 0; i < rowCount; i++)
        {
            double leftW  = i < inputs.Count  ? MeasureWidth(inputs[i].Name,  10, FontWeight.Normal) : 0;
            double rightW = i < outputs.Count ? MeasureWidth(outputs[i].Name, 10, FontWeight.Normal) : 0;
            double w = leftW + rightW
                     + 2 * (GraphTheme.PortRadius + 6)   // label-to-pin gap on each side
                     + ColumnGutter;
            if (w > rowRequired) rowRequired = w;
        }

        return Math.Max(Math.Max(MinRegularNodeWidth, node.Width), Math.Max(titleRequired, rowRequired));
    }

    private static double MeasureWidth(string text, double size, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(GraphTheme.FontFamily, FontStyle.Normal, weight),
            size, Brushes.White);
        return ft.Width;
    }
}
