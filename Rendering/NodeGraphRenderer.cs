using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using PoSHBlox.Models;
using PoSHBlox.Services;
using PoSHBlox.ViewModels;

namespace PoSHBlox.Rendering;

/// <summary>
/// Handles all visual rendering for the node graph.
/// Separated from the canvas control so rendering can change without touching input handling.
/// </summary>
public class NodeGraphRenderer
{
    // Reusable pen/brush instances to reduce allocations in the render loop
    private readonly SolidColorBrush _bgBrush = new(GraphTheme.Background);
    private readonly SolidColorBrush _gridDotBrush = new(GraphTheme.GridMinor);
    private readonly SolidColorBrush _gridDotMajorBrush = new(GraphTheme.GridMajor);
    private readonly Pen _wirePen = new(new SolidColorBrush(GraphTheme.Wire), GraphTheme.WireThickness);

    // Margin added to the viewport for culling (world-space units).
    // Covers port labels (~80px), shadows, selection borders, and breathing room.
    private const double CullMargin = 100;

    /// <summary>
    /// Render the entire graph: background, grid, wires, containers, nodes, and HUD.
    /// </summary>
    public void Render(DrawingContext ctx, Rect bounds, GraphCanvasViewModel vm,
        bool isDraggingWire, NodePort? wireStartPort, Point wireEndPoint,
        bool isDraggingNode = false, GraphNode? dragNode = null)
    {
        ctx.DrawRectangle(_bgBrush, null, bounds);
        DrawGrid(ctx, bounds, vm);

        // Compute the visible viewport in world-space coordinates, with generous margin
        var viewport = GetWorldViewport(bounds, vm);

        using (ctx.PushTransform(Matrix.CreateTranslation(vm.PanX, vm.PanY)))
        using (ctx.PushTransform(Matrix.CreateScale(vm.Zoom, vm.Zoom)))
        {
            // Containers: sorted by nesting depth (parents before children)
            foreach (var node in vm.Nodes
                .Where(n => n.IsContainer)
                .OrderBy(n => GetNestingDepth(n)))
            {
                if (IsNodeVisible(node, viewport))
                    DrawContainer(ctx, node, isDraggingNode, dragNode, wireStartPort);
            }

            // Wires above containers
            foreach (var conn in vm.Connections)
            {
                if (IsWireVisible(conn, viewport))
                    DrawWire(ctx, conn);
            }

            if (isDraggingWire && wireStartPort != null)
                DrawPendingWire(ctx, wireStartPort, wireEndPoint);

            // Regular nodes on top
            foreach (var node in vm.Nodes.Where(n => !n.IsContainer))
            {
                if (IsNodeVisible(node, viewport))
                    DrawNode(ctx, node, wireStartPort);
            }
        }

        DrawHud(ctx, bounds, vm);
    }

    // ── Viewport culling helpers ─────────────────────────────

    private static Rect GetWorldViewport(Rect screenBounds, GraphCanvasViewModel vm)
    {
        double x = -vm.PanX / vm.Zoom - CullMargin;
        double y = -vm.PanY / vm.Zoom - CullMargin;
        double w = screenBounds.Width / vm.Zoom + CullMargin * 2;
        double h = screenBounds.Height / vm.Zoom + CullMargin * 2;
        return new Rect(x, y, w, h);
    }

    private static bool IsNodeVisible(GraphNode node, Rect viewport)
    {
        double w = node.IsContainer ? node.ContainerWidth : NodeLayout.GetEffectiveWidth(node);
        double h = node.IsContainer ? node.ContainerHeight : node.Height;
        var nodeRect = new Rect(node.X, node.Y, w, h);
        return viewport.Intersects(nodeRect);
    }

    private static bool IsWireVisible(NodeConnection conn, Rect viewport)
    {
        var start = GetPortPosition(conn.Source.Owner!, conn.Source);
        var end = GetPortPosition(conn.Target.Owner!, conn.Target);

        // Build the bounding box of all four Bézier control points
        double dx = Math.Max(Math.Abs(end.X - start.X) * 0.5, 50);
        double cp1X = start.X + dx;
        double cp2X = end.X - dx;

        double minX = Math.Min(Math.Min(start.X, end.X), Math.Min(cp1X, cp2X));
        double maxX = Math.Max(Math.Max(start.X, end.X), Math.Max(cp1X, cp2X));
        double minY = Math.Min(start.Y, end.Y);
        double maxY = Math.Max(start.Y, end.Y);

        var wireBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        return viewport.Intersects(wireBounds);
    }

    // ── Grid ───────────────────────────────────────────────────

    private void DrawGrid(DrawingContext ctx, Rect bounds, GraphCanvasViewModel vm)
    {
        double sg = GraphTheme.GridSize * vm.Zoom;
        if (sg < 8) return; // Don't draw grid when zoomed way out

        // Use integer grid indices to avoid floating-point drift
        int firstCol = (int)Math.Floor(-vm.PanX / sg);
        int lastCol  = (int)Math.Ceiling((bounds.Width - vm.PanX) / sg);
        int firstRow = (int)Math.Floor(-vm.PanY / sg);
        int lastRow  = (int)Math.Ceiling((bounds.Height - vm.PanY) / sg);

        for (int col = firstCol; col <= lastCol; col++)
        {
            double x = col * sg + vm.PanX;
            bool majorCol = col % GraphTheme.GridMajorEvery == 0;

            for (int row = firstRow; row <= lastRow; row++)
            {
                double y = row * sg + vm.PanY;
                bool major = majorCol && row % GraphTheme.GridMajorEvery == 0;

                var brush = major ? _gridDotMajorBrush : _gridDotBrush;
                double r = major ? GraphTheme.GridDotMajorRadius : GraphTheme.GridDotRadius;
                ctx.DrawEllipse(brush, null, new Point(x, y), r, r);
            }
        }
    }

    // ── Regular nodes ──────────────────────────────────────────

    private void DrawNode(DrawingContext ctx, GraphNode node, NodePort? wireStartPort = null)
    {
        double hh = GraphNode.HeaderHeight;
        double width = NodeLayout.GetEffectiveWidth(node);
        var rect = new Rect(node.X, node.Y, width, node.Height);
        var catColor = GraphTheme.GetCategoryColor(node.Category);
        bool inContainer = node.ParentContainer != null;

        // Shadow
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.NodeShadow), null,
            new RoundedRect(rect.Translate(new Vector(GraphTheme.NodeShadowOffset, GraphTheme.NodeShadowOffset)),
                GraphTheme.NodeCornerRadius));

        // Body
        // Border priority: selection > error > warning > default. Validation
        // borders win over the "inside container" tint but lose to explicit
        // selection so the user can always see what they've clicked on.
        var borderColor =
            node.IsSelected ? GraphTheme.NodeSelectedBorder
            : node.HasErrors ? GraphTheme.NodeErrorBorder
            : node.HasIssues ? GraphTheme.NodeWarningBorder
            : inContainer ? catColor
            : GraphTheme.NodeBorder;
        double borderThickness = node.IsSelected ? 2.5 : (node.HasIssues ? 2.0 : 1.0);
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.NodeBackground),
            new Pen(new SolidColorBrush(borderColor), borderThickness),
            new RoundedRect(rect, GraphTheme.NodeCornerRadius));

        // Header band with gradient overlay
        using (ctx.PushClip(new RoundedRect(rect, GraphTheme.NodeCornerRadius)))
        {
            var headerRect = new Rect(node.X, node.Y, width, hh);
            ctx.DrawRectangle(new SolidColorBrush(catColor), null, headerRect);
            ctx.DrawRectangle(MakeHeaderGradient(), null, headerRect);
        }

        // Title + category badge
        var title = MakeText(node.Title, 13, FontWeight.Bold, GraphTheme.TextPrimary);
        ctx.DrawText(title, new Point(node.X + 14, node.Y + (hh - title.Height) / 2));

        // Collapse-state chevron on the header (right side, dim). Indicates the
        // node can be collapsed/expanded — wired to the C keyboard shortcut /
        // upcoming context menu.
        var chevronGlyph = node.IsCollapsed ? "\u25B8" : "\u25BE"; // ▸ / ▾
        var chevron = MakeText(chevronGlyph, 11, FontWeight.Normal, GraphTheme.TextSecondary);
        ctx.DrawText(chevron, new Point(node.X + width - 18, node.Y + (hh - chevron.Height) / 2));

        // Validation badge — sits left of the chevron when the node has issues.
        // Color mirrors the border (red for errors, amber for warning-only).
        // Drawn on a square dark chip with a matching-color ring so it stays
        // legible on category headers that share the amber/red hue family
        // (Output's header would otherwise swallow an amber warning badge).
        if (node.HasIssues)
        {
            var badgeColor = node.HasErrors ? GraphTheme.NodeErrorBorder : GraphTheme.NodeWarningBorder;
            var badge = MakeText("\u26A0", 12, FontWeight.Bold, badgeColor);   // ⚠

            // Square chip sized to the larger glyph dimension + padding so the
            // glyph sits centered in a square rather than an elongated pill.
            double chipSize = Math.Max(badge.Width, badge.Height) + 6;
            double chipX = node.X + width - 42;
            double chipY = node.Y + (hh - chipSize) / 2;

            ctx.DrawRectangle(
                new SolidColorBrush(GraphTheme.NodeBackground),
                new Pen(new SolidColorBrush(badgeColor), 1),
                new RoundedRect(new Rect(chipX, chipY, chipSize, chipSize), 3));

            // Center the glyph inside the chip.
            double glyphX = chipX + (chipSize - badge.Width) / 2;
            double glyphY = chipY + (chipSize - badge.Height) / 2;
            ctx.DrawText(badge, new Point(glyphX, glyphY));
        }

        // Collapsed-hint row: "+N hidden" centered under the visible data rows.
        if (node.HiddenDataInputCount > 0)
        {
            var hint = MakeText($"+{node.HiddenDataInputCount} hidden", 10, FontWeight.Normal, GraphTheme.HudText);
            double hintY = node.Y + node.Height - hint.Height - 8;
            ctx.DrawText(hint, new Point(node.X + (width - hint.Width) / 2, hintY));
        }

        // Ports
        foreach (var port in node.Inputs) DrawPort(ctx, node, port, wireStartPort);
        foreach (var port in node.Outputs) DrawPort(ctx, node, port, wireStartPort);
    }

    // ── Containers ─────────────────────────────────────────────

    private void DrawContainer(DrawingContext ctx, GraphNode node,
        bool isDraggingNode = false, GraphNode? dragNode = null,
        NodePort? wireStartPort = null)
    {
        var catColor = GraphTheme.GetCategoryColor(node.Category);
        double hh = GraphNode.ContainerHeaderHeight;
        var rect = new Rect(node.X, node.Y, node.ContainerWidth, node.ContainerHeight);

        // Shadow
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.ContainerShadow), null,
            new RoundedRect(rect.Translate(new Vector(GraphTheme.ContainerShadowOffset, GraphTheme.ContainerShadowOffset)),
                GraphTheme.ContainerCornerRadius));

        // Body (dashed for control flow, solid for functions and labels)
        var borderColor = node.IsSelected ? GraphTheme.NodeSelectedBorder : catColor;
        bool solidBorder = node.ContainerType is ContainerType.Function or ContainerType.Label;
        var borderPen = solidBorder
            ? new Pen(new SolidColorBrush(borderColor), node.IsSelected ? 2.5 : 1.5)
            : new Pen(new SolidColorBrush(borderColor), node.IsSelected ? 2.5 : 1.5) { DashStyle = DashStyle.Dash };
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.ContainerBg), borderPen,
            new RoundedRect(rect, GraphTheme.ContainerCornerRadius));

        // Header band
        using (ctx.PushClip(new RoundedRect(rect, GraphTheme.ContainerCornerRadius)))
        {
            var headerRect = new Rect(node.X, node.Y, node.ContainerWidth, hh);
            ctx.DrawRectangle(new SolidColorBrush(catColor), null, headerRect);
            ctx.DrawRectangle(MakeHeaderGradient(), null, headerRect);
        }

        // Title + badge
        var title = MakeText(node.Title, 14, FontWeight.Bold, GraphTheme.TextPrimary);
        ctx.DrawText(title, new Point(node.X + 14, node.Y + (hh - title.Height) / 2));

        // Zones — highlight zone under cursor when dragging a node
        foreach (var zone in node.Zones)
        {
            bool highlight = isDraggingNode && dragNode != null
                && dragNode != node // don't highlight own zones
                && IsPointInZone(node, zone, dragNode.X + dragNode.EffectiveWidth / 2, dragNode.Y + dragNode.Height / 2);
            DrawZone(ctx, node, zone, highlight);
        }

        // Resize grip (bottom-right triangle)
        DrawResizeGrip(ctx, node);

        // Ports
        foreach (var port in node.Inputs) DrawPort(ctx, node, port, wireStartPort);
        foreach (var port in node.Outputs) DrawPort(ctx, node, port, wireStartPort);
    }

    private void DrawZone(DrawingContext ctx, GraphNode parent, ContainerZone zone, bool highlight = false)
    {
        var (zx, zy, zw, zh) = zone.GetAbsoluteRect(parent);

        // Zone background — use highlight color and solid teal border when a node is being dragged over
        var bgColor = highlight ? GraphTheme.ZoneDropHighlight : GraphTheme.ZoneBg;
        var borderPen = highlight
            ? new Pen(new SolidColorBrush(GraphTheme.NodeSelectedBorder), 1.5)
            : new Pen(new SolidColorBrush(GraphTheme.ZoneBorder), 1) { DashStyle = DashStyle.Dot };
        ctx.DrawRectangle(new SolidColorBrush(bgColor), borderPen,
            new RoundedRect(new Rect(zx, zy, zw, zh), 6));

        // Zone label
        var label = MakeText(zone.Name.ToUpperInvariant(), 10, FontWeight.SemiBold, GraphTheme.ZoneLabel);
        ctx.DrawText(label, new Point(zx + 8, zy + 4));

        // Empty hint
        if (zone.Children.Count == 0)
        {
            var hint = new FormattedText("Drop nodes here", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(GraphTheme.FontFamily, FontStyle.Italic),
                11, new SolidColorBrush(GraphTheme.ZoneHint));
            ctx.DrawText(hint, new Point(zx + (zw - hint.Width) / 2, zy + (zh - hint.Height) / 2));
        }
    }

    /// <summary>
    /// Check if a point (in canvas space) is inside a container zone.
    /// </summary>
    private static bool IsPointInZone(GraphNode container, ContainerZone zone, double px, double py)
    {
        var (zx, zy, zw, zh) = zone.GetAbsoluteRect(container);
        return px >= zx && px <= zx + zw && py >= zy && py <= zy + zh;
    }

    private static void DrawResizeGrip(DrawingContext ctx, GraphNode node)
    {
        double hx = node.X + node.ContainerWidth;
        double hy = node.Y + node.ContainerHeight;
        double gs = GraphTheme.ResizeGripSize;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(hx, hy), true);
            g.LineTo(new Point(hx - gs, hy));
            g.LineTo(new Point(hx, hy - gs));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(GraphTheme.ResizeGrip), null, geo);
    }

    // ── Ports ──────────────────────────────────────────────────

    /// <summary>
    /// Dim alpha applied to ports/labels that are incompatible with the current
    /// wire-drag source. Chosen to read as clearly backgrounded without fully
    /// disappearing.
    /// </summary>
    private const byte DimAlpha = 70;

    private void DrawPort(DrawingContext ctx, GraphNode node, NodePort port, NodePort? wireStartPort)
    {
        // Collapsed nodes hide non-essential data input pins — don't draw them.
        if (!node.IsPortVisible(port)) return;

        var pos = GetPortPosition(node, port);
        bool isInput = port.Direction == PortDirection.Input;

        // Compatibility dim: when a wire drag is active, port stays bright if
        // it's the drag source itself or could accept the drag; otherwise dim.
        bool dragActive = wireStartPort != null;
        bool isSelf = dragActive && ReferenceEquals(wireStartPort, port);
        bool compatible = !dragActive
            || isSelf
            || PortCompatibility.CanConnect(wireStartPort!, port);
        byte alpha = compatible ? (byte)255 : DimAlpha;

        if (port.Kind == PortKind.Exec)
        {
            DrawExecTriangle(ctx, pos, alpha);
            return;
        }

        var baseColor = GraphTheme.GetDataTypeColor(port.DataType);
        var color = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        var centerColor = Color.FromArgb(alpha, GraphTheme.PortCenter.R, GraphTheme.PortCenter.G, GraphTheme.PortCenter.B);

        // Outer ring + inner dot, colored by data type.
        ctx.DrawEllipse(new SolidColorBrush(centerColor),
            new Pen(new SolidColorBrush(color), 2),
            pos, GraphTheme.PortRadius, GraphTheme.PortRadius);
        ctx.DrawEllipse(new SolidColorBrush(color), null,
            pos, GraphTheme.PortDotRadius, GraphTheme.PortDotRadius);

        // Label — skip for unnamed pins (e.g. ForEach's synthesized Source).
        if (string.IsNullOrEmpty(port.Name)) return;

        var labelColor = Color.FromArgb(alpha, GraphTheme.PortLabel.R, GraphTheme.PortLabel.G, GraphTheme.PortLabel.B);
        var label = MakeText(port.Name, 10, FontWeight.Normal, labelColor);
        double labelX = isInput
            ? pos.X + GraphTheme.PortRadius + 6
            : pos.X - GraphTheme.PortRadius - label.Width - 6;
        ctx.DrawText(label, new Point(labelX, pos.Y - label.Height / 2));
    }

    /// <summary>
    /// Right-pointing filled triangle at <paramref name="pos"/>. Blueprint-style
    /// exec-pin glyph. Both input (left edge) and output (right edge) point right
    /// to cue data-flow direction. Alpha controls dim-state during wire drags.
    /// </summary>
    private static void DrawExecTriangle(DrawingContext ctx, Point pos, byte alpha = 255)
    {
        double size = GraphTheme.PortRadius + 1;
        double tipX = pos.X + size;
        double baseX = pos.X - size;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(baseX, pos.Y - size), true);
            g.LineTo(new Point(tipX, pos.Y));
            g.LineTo(new Point(baseX, pos.Y + size));
            g.EndFigure(true);
        }
        var color = Color.FromArgb(alpha, GraphTheme.ExecPin.R, GraphTheme.ExecPin.G, GraphTheme.ExecPin.B);
        var brush = new SolidColorBrush(color);
        ctx.DrawGeometry(brush, new Pen(brush, 1.5), geo);
    }

    // ── Wires ──────────────────────────────────────────────────

    private void DrawWire(DrawingContext ctx, NodeConnection conn)
    {
        var start = GetPortPosition(conn.Source.Owner!, conn.Source);
        var end = GetPortPosition(conn.Target.Owner!, conn.Target);
        ctx.DrawGeometry(null, _wirePen, MakeBezier(start, end));
    }

    private void DrawPendingWire(DrawingContext ctx, NodePort startPort, Point endPoint)
    {
        var start = GetPortPosition(startPort.Owner!, startPort);
        var end = endPoint;
        if (startPort.Direction == PortDirection.Input) (start, end) = (end, start);

        // Color the in-flight wire by the source pin's type so users see what
        // they'd be producing. Exec drags use the cream exec color; data drags
        // use the typed palette entry.
        var wireColor = startPort.Kind == PortKind.Exec
            ? GraphTheme.ExecPin
            : GraphTheme.GetDataTypeColor(startPort.DataType);

        var pen = new Pen(new SolidColorBrush(wireColor), GraphTheme.WireThickness)
        {
            DashStyle = DashStyle.Dash,
        };
        ctx.DrawGeometry(null, pen, MakeBezier(start, end));
    }

    // ── HUD ────────────────────────────────────────────────────

    private void DrawHud(DrawingContext ctx, Rect bounds, GraphCanvasViewModel vm)
    {
        var text = MakeText(
            $"Zoom: {vm.Zoom:P0}  |  Nodes: {vm.Nodes.Count}  |  Middle-click: Pan  |  Scroll: Zoom  |  Right-click wire: Delete",
            11, FontWeight.Normal, GraphTheme.HudText);

        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.HudBg), null,
            new Rect(0, bounds.Height - 28, bounds.Width, 28));
        ctx.DrawText(text, new Point(10, bounds.Height - 22));
    }

    // ── Geometry helpers (public for hit testing) ──────────────

    /// <summary>
    /// Calculate absolute position of a port on the canvas. V2 layout:
    ///   - Exec pins ride in the header (input left edge, output right edge).
    ///   - Data pins occupy body rows, indexed within DataInputs/DataOutputs only.
    /// Used by both rendering and hit testing.
    /// </summary>
    public static Point GetPortPosition(GraphNode node, NodePort port)
    {
        double headerH = node.IsContainer ? GraphNode.ContainerHeaderHeight : GraphNode.HeaderHeight;
        double width = node.IsContainer ? node.ContainerWidth : NodeLayout.GetEffectiveWidth(node);

        // Containers still put exec pins in the header (they're wider and the
        // chevron pattern doesn't apply). Regular nodes get a dedicated exec row.
        if (port.Kind == PortKind.Exec)
        {
            // Keep the triangle tips clear of the node's rounded corners
            // (NodeCornerRadius=8) and give a little visual breathing room.
            const double inset = 16;
            double x = port.Direction == PortDirection.Input ? node.X + inset : node.X + width - inset;
            double y;
            if (node.IsContainer)
            {
                y = node.Y + headerH / 2;
            }
            else
            {
                // Centered vertically in the exec row sitting directly below the header.
                y = node.Y + headerH + GraphNode.ExecRowHeight / 2;
            }
            return new Point(x, y);
        }

        // Data pin: index within VisibleDataInputs (so collapsed hidden rows don't
        // reserve space) or DataOutputs. Returns an off-screen point for pins not
        // in the visible list — keeps them out of the way of hit testing too.
        int idx = -1, i = 0;
        var list = port.Direction == PortDirection.Input ? node.VisibleDataInputs : node.DataOutputs;
        foreach (var p in list)
        {
            if (ReferenceEquals(p, port)) { idx = i; break; }
            i++;
        }
        if (idx < 0) return new Point(-1e6, -1e6);

        // Data rows start below the header, plus the exec row if one is present.
        double execOffset = (!node.IsContainer && node.HasExecRow) ? GraphNode.ExecRowHeight : 0;
        double xd = port.Direction == PortDirection.Input ? node.X : node.X + width;
        double yd = node.Y + headerH + execOffset + GraphNode.PortSpacing + idx * GraphNode.PortSpacing;
        return new Point(xd, yd);
    }

    /// <summary>
    /// Sample a point along a Bézier curve at parameter t (0..1).
    /// Used for wire hit testing.
    /// </summary>
    public static Point SampleBezier(Point start, Point end, double t)
    {
        double dx = Math.Abs(end.X - start.X) * 0.5;
        double mt = 1 - t;
        return new Point(
            mt * mt * mt * start.X + 3 * mt * mt * t * (start.X + dx) + 3 * mt * t * t * (end.X - dx) + t * t * t * end.X,
            mt * mt * mt * start.Y + 3 * mt * mt * t * start.Y + 3 * mt * t * t * end.Y + t * t * t * end.Y);
    }

    // ── Private helpers ────────────────────────────────────────

    private static StreamGeometry MakeBezier(Point start, Point end)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double dx = Math.Max(Math.Abs(end.X - start.X) * 0.5, 50);
            ctx.BeginFigure(start, false);
            ctx.CubicBezierTo(
                new Point(start.X + dx, start.Y),
                new Point(end.X - dx, end.Y),
                end);
        }
        return geo;
    }

    private static LinearGradientBrush MakeHeaderGradient() => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(GraphTheme.HeaderGradientTop, 0),
            new GradientStop(GraphTheme.HeaderGradientBottom, 1),
        }
    };

    private static int GetNestingDepth(GraphNode node)
    {
        int depth = 0;
        var current = node.ParentContainer;
        while (current != null) { depth++; current = current.ParentContainer; }
        return depth;
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Color color)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(GraphTheme.FontFamily, FontStyle.Normal, weight),
            size, new SolidColorBrush(color));
}
