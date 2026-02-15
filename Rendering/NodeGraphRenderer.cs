using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using PoSHBlox.Models;
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
    private readonly Pen _wirePendingPen = new(new SolidColorBrush(GraphTheme.WirePending), GraphTheme.WireThickness) { DashStyle = DashStyle.Dash };

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
                    DrawContainer(ctx, node, isDraggingNode, dragNode);
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
                    DrawNode(ctx, node);
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
        double w = node.IsContainer ? node.ContainerWidth : node.Width;
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

    private void DrawNode(DrawingContext ctx, GraphNode node)
    {
        double hh = GraphNode.HeaderHeight;
        var rect = new Rect(node.X, node.Y, node.Width, node.Height);
        var catColor = GraphTheme.GetCategoryColor(node.Category);
        bool inContainer = node.ParentContainer != null;

        // Shadow
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.NodeShadow), null,
            new RoundedRect(rect.Translate(new Vector(GraphTheme.NodeShadowOffset, GraphTheme.NodeShadowOffset)),
                GraphTheme.NodeCornerRadius));

        // Body
        var borderColor = node.IsSelected ? GraphTheme.NodeSelectedBorder
            : inContainer ? catColor
            : GraphTheme.NodeBorder;
        ctx.DrawRectangle(new SolidColorBrush(GraphTheme.NodeBackground),
            new Pen(new SolidColorBrush(borderColor), node.IsSelected ? 2.5 : 1),
            new RoundedRect(rect, GraphTheme.NodeCornerRadius));

        // Header band with gradient overlay
        using (ctx.PushClip(new RoundedRect(rect, GraphTheme.NodeCornerRadius)))
        {
            var headerRect = new Rect(node.X, node.Y, node.Width, hh);
            ctx.DrawRectangle(new SolidColorBrush(catColor), null, headerRect);
            ctx.DrawRectangle(MakeHeaderGradient(), null, headerRect);
        }

        // Title + category badge
        var title = MakeText(node.Title, 13, FontWeight.Bold, GraphTheme.TextPrimary);
        ctx.DrawText(title, new Point(node.X + 14, node.Y + (hh - title.Height) / 2));

        // Ports
        foreach (var port in node.Inputs) DrawPort(ctx, node, port);
        foreach (var port in node.Outputs) DrawPort(ctx, node, port);
    }

    // ── Containers ─────────────────────────────────────────────

    private void DrawContainer(DrawingContext ctx, GraphNode node,
        bool isDraggingNode = false, GraphNode? dragNode = null)
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
        foreach (var port in node.Inputs) DrawPort(ctx, node, port);
        foreach (var port in node.Outputs) DrawPort(ctx, node, port);
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

    private void DrawPort(DrawingContext ctx, GraphNode node, NodePort port)
    {
        var pos = GetPortPosition(node, port);
        bool isInput = port.Direction == PortDirection.Input;
        var color = isInput ? GraphTheme.PortInput : GraphTheme.PortOutput;

        // Outer ring
        ctx.DrawEllipse(new SolidColorBrush(GraphTheme.PortCenter),
            new Pen(new SolidColorBrush(color), 2),
            pos, GraphTheme.PortRadius, GraphTheme.PortRadius);

        // Inner dot
        ctx.DrawEllipse(new SolidColorBrush(color), null,
            pos, GraphTheme.PortDotRadius, GraphTheme.PortDotRadius);

        // Label
        var label = MakeText(port.Name, 10, FontWeight.Normal, GraphTheme.PortLabel);
        double labelX = isInput
            ? pos.X + GraphTheme.PortRadius + 6
            : pos.X - GraphTheme.PortRadius - label.Width - 6;
        ctx.DrawText(label, new Point(labelX, pos.Y - label.Height / 2));
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
        ctx.DrawGeometry(null, _wirePendingPen, MakeBezier(start, end));
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
    /// Calculate absolute position of a port on the canvas.
    /// Used by both rendering and hit testing.
    /// </summary>
    public static Point GetPortPosition(GraphNode node, NodePort port)
    {
        double headerH = node.IsContainer ? GraphNode.ContainerHeaderHeight : GraphNode.HeaderHeight;
        double width = node.IsContainer ? node.ContainerWidth : node.Width;
        int idx = port.Direction == PortDirection.Input
            ? node.Inputs.IndexOf(port)
            : node.Outputs.IndexOf(port);

        double x = port.Direction == PortDirection.Input ? node.X : node.X + width;
        double y = node.Y + headerH + GraphNode.PortSpacing + idx * GraphNode.PortSpacing;

        return new Point(x, y);
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
