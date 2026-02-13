using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PoSHBlox.Models;
using PoSHBlox.Rendering;
using PoSHBlox.ViewModels;

namespace PoSHBlox.Controls;

/// <summary>
/// Custom canvas control for the node graph.
/// Handles all user input (pan, zoom, drag, connect, resize).
/// Delegates all rendering to NodeGraphRenderer.
/// </summary>
public class NodeGraphCanvas : Control
{
    private GraphCanvasViewModel? _vm;
    private readonly NodeGraphRenderer _renderer = new();
    private bool _isAttached;

    // ── Interaction state ──────────────────────────────────────
    private bool _isPanning;
    private Point _panStart;

    private bool _isDraggingNode;
    private GraphNode? _dragNode;
    private Point _dragOffset;
    private Point _dragStartPos;

    private bool _isDraggingWire;
    private NodePort? _wireStartPort;
    private Point _wireEndPoint;

    private bool _isResizingContainer;
    private GraphNode? _resizeNode;

    public NodeGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    // ── Lifecycle ──────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        DispatcherTimer.Run(() => { InvalidateVisual(); return _isAttached; }, TimeSpan.FromMilliseconds(16));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as GraphCanvasViewModel;
    }

    // ── Input: Pointer Press ───────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm == null) return;

        var screenPos = e.GetPosition(this);
        var canvasPos = ScreenToCanvas(screenPos);
        var props = e.GetCurrentPoint(this).Properties;

        // Middle-click: start panning
        if (props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStart = screenPos;
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            // Check resize grip first (containers only)
            var resizeTarget = HitResizeGrip(canvasPos);
            if (resizeTarget != null)
            {
                _isResizingContainer = true;
                _resizeNode = resizeTarget;
                e.Handled = true;
                return;
            }

            // Check port hit (start wire drag)
            var port = HitPort(canvasPos);
            if (port != null)
            {
                _isDraggingWire = true;
                _wireStartPort = port;
                _wireEndPoint = canvasPos;
                e.Handled = true;
                return;
            }

            // Check node hit (start node drag)
            var node = HitNode(canvasPos);
            if (node != null)
            {
                _vm.SelectNode(node);
                _isDraggingNode = true;
                _dragNode = node;
                _dragOffset = new Point(canvasPos.X - node.X, canvasPos.Y - node.Y);
                _dragStartPos = new Point(node.X, node.Y);
                e.Handled = true;
                return;
            }

            // Clicked empty space: deselect
            _vm.SelectNode(null);
        }

        // Right-click: delete wire
        if (props.IsRightButtonPressed)
        {
            var conn = HitConnection(canvasPos);
            if (conn != null)
            {
                _vm.Connections.Remove(conn);
                e.Handled = true;
            }
        }
    }

    // ── Input: Pointer Move ────────────────────────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm == null) return;

        var screenPos = e.GetPosition(this);
        var canvasPos = ScreenToCanvas(screenPos);

        if (_isPanning)
        {
            var delta = screenPos - _panStart;
            _vm.PanX += delta.X;
            _vm.PanY += delta.Y;
            _panStart = screenPos;
            return;
        }

        if (_isResizingContainer && _resizeNode != null)
        {
            _resizeNode.ContainerWidth = Math.Max(300, canvasPos.X - _resizeNode.X);
            _resizeNode.ContainerHeight = Math.Max(200, canvasPos.Y - _resizeNode.Y);
            _resizeNode.RecalcZoneLayout();
            return;
        }

        if (_isDraggingNode && _dragNode != null)
        {
            _dragNode.X = Math.Round((canvasPos.X - _dragOffset.X) / 10) * 10;
            _dragNode.Y = Math.Round((canvasPos.Y - _dragOffset.Y) / 10) * 10;
            return;
        }

        if (_isDraggingWire)
        {
            _wireEndPoint = canvasPos;
            return;
        }

        // Update cursor based on what's under the pointer
        var hoveredPort = HitPort(canvasPos);
        var hoveredResize = HitResizeGrip(canvasPos);
        Cursor = hoveredPort != null ? new Cursor(StandardCursorType.Hand)
            : hoveredResize != null ? new Cursor(StandardCursorType.BottomRightCorner)
            : Cursor.Default;
    }

    // ── Input: Pointer Release ─────────────────────────────────

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm == null) return;

        // Complete wire connection
        if (_isDraggingWire && _wireStartPort != null)
        {
            var canvasPos = ScreenToCanvas(e.GetPosition(this));
            var targetPort = HitPort(canvasPos);

            if (targetPort != null && targetPort != _wireStartPort)
            {
                if (_wireStartPort.Direction == PortDirection.Output && targetPort.Direction == PortDirection.Input)
                    _vm.AddConnection(_wireStartPort, targetPort);
                else if (_wireStartPort.Direction == PortDirection.Input && targetPort.Direction == PortDirection.Output)
                    _vm.AddConnection(targetPort, _wireStartPort);
            }
        }

        // Snap dropped node into container zone (only if it actually moved)
        if (_isDraggingNode && _dragNode != null && !_dragNode.IsContainer)
        {
            double dx = _dragNode.X - _dragStartPos.X;
            double dy = _dragNode.Y - _dragStartPos.Y;
            if (dx * dx + dy * dy > 25) // moved more than ~5px
                TrySnapToZone(_dragNode);
        }

        // Reset all interaction state
        _isPanning = false;
        _isDraggingNode = false;
        _dragNode = null;
        _isDraggingWire = false;
        _wireStartPort = null;
        _isResizingContainer = false;
        _resizeNode = null;
    }

    // ── Input: Scroll (zoom) ───────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm == null) return;

        var screenPos = e.GetPosition(this);
        var canvasBefore = ScreenToCanvas(screenPos);

        _vm.Zoom *= e.Delta.Y > 0 ? 1.1 : 0.9;
        _vm.ClampZoom();

        var canvasAfter = ScreenToCanvas(screenPos);
        _vm.PanX += (canvasAfter.X - canvasBefore.X) * _vm.Zoom;
        _vm.PanY += (canvasAfter.Y - canvasBefore.Y) * _vm.Zoom;

        e.Handled = true;
    }

    // ── Zone snapping ──────────────────────────────────────────

    private void TrySnapToZone(GraphNode node)
    {
        if (_vm == null) return;

        // Remember previous zone membership
        var prevZone = node.ParentZone;
        int prevIndex = prevZone?.Children.IndexOf(node) ?? -1;

        // Remove from previous zone
        if (node.ParentContainer != null && node.ParentZone != null)
        {
            node.ParentZone.Children.Remove(node);
            node.ParentContainer = null;
            node.ParentZone = null;
        }

        double nodeCenterX = node.X + node.Width / 2;
        double nodeCenterY = node.Y + node.Height / 2;

        foreach (var container in _vm.Nodes.Where(n => n.IsContainer))
        {
            foreach (var zone in container.Zones)
            {
                var (zx, zy, zw, zh) = zone.GetAbsoluteRect(container);
                if (nodeCenterX >= zx && nodeCenterX <= zx + zw &&
                    nodeCenterY >= zy && nodeCenterY <= zy + zh)
                {
                    node.ParentContainer = container;
                    node.ParentZone = zone;

                    // If returning to the same zone, restore original index
                    if (zone == prevZone && prevIndex >= 0 && prevIndex <= zone.Children.Count)
                        zone.Children.Insert(prevIndex, node);
                    else if (!zone.Children.Contains(node))
                        zone.Children.Add(node);

                    // Snap: stack vertically inside zone with padding
                    double snapPad = 10;
                    double snapTop = zy + GraphNode.ZoneHeaderHeight + snapPad;
                    int childIndex = zone.Children.IndexOf(node);
                    for (int i = 0; i < childIndex; i++)
                        snapTop += zone.Children[i].Height + snapPad;

                    node.X = Math.Round((zx + snapPad) / 10) * 10;
                    node.Y = Math.Round(snapTop / 10) * 10;

                    if (node.Width > zw - snapPad * 2)
                        node.Width = zw - snapPad * 2;

                    return;
                }
            }
        }
    }

    // ── Hit testing ────────────────────────────────────────────

    private Point ScreenToCanvas(Point screen)
    {
        if (_vm == null) return screen;
        return new Point(
            (screen.X - _vm.PanX) / _vm.Zoom,
            (screen.Y - _vm.PanY) / _vm.Zoom);
    }

    private GraphNode? HitNode(Point canvasPos)
    {
        if (_vm == null) return null;

        // Regular nodes (on top, check last-to-first for z-order)
        for (int i = _vm.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _vm.Nodes[i];
            if (n.IsContainer) continue;
            if (canvasPos.X >= n.X && canvasPos.X <= n.X + n.Width &&
                canvasPos.Y >= n.Y && canvasPos.Y <= n.Y + n.Height)
                return n;
        }

        // Container headers only (drag by header)
        foreach (var n in _vm.Nodes.Where(n => n.IsContainer))
        {
            if (canvasPos.X >= n.X && canvasPos.X <= n.X + n.ContainerWidth &&
                canvasPos.Y >= n.Y && canvasPos.Y <= n.Y + GraphNode.ContainerHeaderHeight)
                return n;
        }

        return null;
    }

    private NodePort? HitPort(Point canvasPos)
    {
        if (_vm == null) return null;

        double hitRadius = GraphTheme.PortRadius + GraphTheme.PortHitPadding;
        foreach (var node in _vm.Nodes)
        {
            foreach (var port in node.Inputs.Concat(node.Outputs))
            {
                var portPos = NodeGraphRenderer.GetPortPosition(node, port);
                double dist = Math.Sqrt(
                    Math.Pow(canvasPos.X - portPos.X, 2) +
                    Math.Pow(canvasPos.Y - portPos.Y, 2));
                if (dist <= hitRadius)
                    return port;
            }
        }
        return null;
    }

    private NodeConnection? HitConnection(Point canvasPos)
    {
        if (_vm == null) return null;

        foreach (var conn in _vm.Connections)
        {
            var start = NodeGraphRenderer.GetPortPosition(conn.Source.Owner!, conn.Source);
            var end = NodeGraphRenderer.GetPortPosition(conn.Target.Owner!, conn.Target);

            for (double t = 0; t <= 1; t += 0.05)
            {
                var sample = NodeGraphRenderer.SampleBezier(start, end, t);
                double dist = Math.Sqrt(
                    Math.Pow(canvasPos.X - sample.X, 2) +
                    Math.Pow(canvasPos.Y - sample.Y, 2));
                if (dist < 8) return conn;
            }
        }
        return null;
    }

    private GraphNode? HitResizeGrip(Point canvasPos)
    {
        if (_vm == null) return null;

        foreach (var node in _vm.Nodes.Where(n => n.IsContainer))
        {
            double rx = node.X + node.ContainerWidth - GraphTheme.ResizeGripSize;
            double ry = node.Y + node.ContainerHeight - GraphTheme.ResizeGripSize;
            if (canvasPos.X >= rx && canvasPos.X <= rx + GraphTheme.ResizeGripSize &&
                canvasPos.Y >= ry && canvasPos.Y <= ry + GraphTheme.ResizeGripSize)
                return node;
        }
        return null;
    }

    // ── Rendering ──────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_vm == null) return;

        _renderer.Render(context, Bounds, _vm,
            _isDraggingWire, _wireStartPort, _wireEndPoint);
    }
}
