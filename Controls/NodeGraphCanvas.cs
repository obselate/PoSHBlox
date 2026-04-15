using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Non-null while rerouting an existing wire: the removed original connection,
    /// restored on release if the drop lands anywhere but a compatible pin.
    /// </summary>
    private NodeConnection? _reroutingOriginal;

    private bool _isResizingContainer;
    private GraphNode? _resizeNode;

    /// <summary>Most recent pointer position in canvas-local coords — used to anchor quick-add on Tab.</summary>
    private Point _lastPointerLocal;

    /// <summary>Pointer position translated into the visual-root's coordinate space (good for positioning popups).</summary>
    public Point CurrentPointerPosition
    {
        get
        {
            if (this.GetVisualRoot() is Visual root)
                return TranslatePoint(_lastPointerLocal, root) ?? _lastPointerLocal;
            return _lastPointerLocal;
        }
    }

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

            // Chevron click → toggle collapse state on the hit node. Checked
            // before the general node hit so dragging near the header-right
            // can still start a node drag; only direct chevron clicks toggle.
            var chevronNode = HitChevron(canvasPos);
            if (chevronNode != null)
            {
                chevronNode.IsCollapsed = !chevronNode.IsCollapsed;
                e.Handled = true;
                return;
            }

            // Check port hit (start wire drag — or reroute an existing wire if
            // the pressed pin already has a connection).
            var port = HitPort(canvasPos);
            if (port != null)
            {
                var existing = _vm.Connections.FirstOrDefault(c =>
                    c.Source == port || c.Target == port);

                if (existing != null)
                {
                    // Pick up the existing wire at this end. Anchor the far end;
                    // the free end follows the cursor. Original is restored on
                    // release if no valid target is hit.
                    _wireStartPort = existing.Source == port ? existing.Target : existing.Source;
                    _reroutingOriginal = existing;
                    _vm.Connections.Remove(existing);
                }
                else
                {
                    _wireStartPort = port;
                    _reroutingOriginal = null;
                }

                _isDraggingWire = true;
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

        // Right-click: open a context menu sized to what's under the cursor —
        // node / wire / empty canvas.
        if (props.IsRightButtonPressed)
        {
            var hitNode = HitNode(canvasPos);
            var hitConn = hitNode == null ? HitConnection(canvasPos) : null;
            if (hitNode != null)
            {
                _vm.SelectNode(hitNode);
                ShowNodeContextMenu(hitNode);
            }
            else if (hitConn != null)
            {
                ShowWireContextMenu(hitConn);
            }
            else
            {
                ShowCanvasContextMenu(canvasPos);
            }
            e.Handled = true;
        }
    }

    // ── Context menus ──────────────────────────────────────────

    private void ShowNodeContextMenu(GraphNode node)
    {
        var items = new List<(string Label, Action? Action, bool IsSeparator)>();

        // Duplicate only applies to non-container nodes (DuplicateSelected
        // already skips containers — don't show a dead menu entry either).
        if (!node.IsContainer)
            items.Add(("Duplicate         Ctrl+D", () => _vm!.DuplicateSelected(), false));
        items.Add(("Delete            Del",        () => _vm!.DeleteSelected(),    false));

        if (!node.IsContainer)
        {
            items.Add(("", null, true));
            items.Add((node.IsCollapsed ? "Expand            C" : "Collapse          C",
                                           () => _vm!.ToggleCollapseSelected(), false));
        }

        ShowFlyout(items);
    }

    private void ShowWireContextMenu(NodeConnection conn)
    {
        ShowFlyout(new List<(string, Action?, bool)>
        {
            ("Delete wire",       () => _vm!.Connections.Remove(conn), false),
        });
    }

    private void ShowCanvasContextMenu(Point canvasPos)
    {
        ShowFlyout(new List<(string, Action?, bool)>
        {
            ("Quick add...      Tab", () => OpenQuickAddAtCursor(),          false),
            ("Add blank node",        () => _vm!.AddNode(),                  false),
            ("",                      null,                                  true),
            ("Reset view        Ctrl+0", () => _vm!.ResetView(),             false),
            ("Zoom to fit all   F",      () => ZoomToFit(selectionOnly: false), false),
        });
    }

    /// <summary>
    /// Open the quick-add popup at the current pointer position, clamped so the
    /// panel stays inside the window. Both Tab and the "Quick add..." menu item
    /// route through here.
    /// </summary>
    public void OpenQuickAddAtCursor(NodePort? source = null)
    {
        if (_vm == null) return;
        OpenQuickAddAt(CurrentPointerPosition, source);
    }

    /// <summary>Clamp <paramref name="windowPoint"/> to keep the popup inside the window, then open.</summary>
    public void OpenQuickAddAt(Point windowPoint, NodePort? source)
    {
        if (_vm == null) return;

        // Match the XAML-declared size. If we ever make this dynamic the clamp
        // can read it from the control once rendered, but constants are fine
        // and avoid a first-frame layout-dependency.
        const double popupW = 380;
        const double popupH = 480;
        const double pad = 8;

        double rootW = 1280, rootH = 720;
        if (this.GetVisualRoot() is Control root)
        {
            rootW = root.Bounds.Width;
            rootH = root.Bounds.Height;
        }

        double x = windowPoint.X;
        double y = windowPoint.Y;
        if (x + popupW + pad > rootW) x = rootW - popupW - pad;
        if (y + popupH + pad > rootH) y = rootH - popupH - pad;
        if (x < pad) x = pad;
        if (y < pad) y = pad;

        _vm.QuickAdd.OpenAt(x, y, source);
    }

    private void ShowFlyout(IList<(string Label, Action? Action, bool IsSeparator)> items)
    {
        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.Pointer,
        };
        foreach (var (label, action, isSeparator) in items)
        {
            if (isSeparator)
            {
                flyout.Items.Add(new Separator());
                continue;
            }
            var mi = new MenuItem { Header = label };
            mi.Classes.Add("graphCtx");
            if (action != null)
                mi.Click += (_, _) => action();
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(this, showAtPointer: true);
    }

    // ── Input: Pointer Move ────────────────────────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm == null) return;

        var screenPos = e.GetPosition(this);
        var canvasPos = ScreenToCanvas(screenPos);
        _lastPointerLocal = screenPos;

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
            // Content-aware minimum: can't resize smaller than children
            double minW = GraphNode.MinContainerWidth;
            double minH = GraphNode.MinContainerHeight;
            foreach (var zone in _resizeNode.Zones)
            {
                var (cw, ch) = zone.GetContentBounds(_resizeNode);
                double pad = GraphNode.ZonePadding;
                if (_resizeNode.Zones.Count == 1)
                {
                    minW = Math.Max(minW, cw + pad * 2);
                    minH = Math.Max(minH, ch + GraphNode.ContainerHeaderHeight + pad);
                }
                else
                {
                    minW = Math.Max(minW, 2 * cw + pad * 3);
                    minH = Math.Max(minH, ch + GraphNode.ContainerHeaderHeight + pad);
                }
            }
            _resizeNode.ContainerWidth = Math.Max(minW, canvasPos.X - _resizeNode.X);
            _resizeNode.ContainerHeight = Math.Max(minH, canvasPos.Y - _resizeNode.Y);
            _resizeNode.RecalcZoneLayout();
            return;
        }

        if (_isDraggingNode && _dragNode != null)
        {
            double newX = Math.Round((canvasPos.X - _dragOffset.X) / 10) * 10;
            double newY = Math.Round((canvasPos.Y - _dragOffset.Y) / 10) * 10;

            // If dragging a container, move all descendants (children, grandchildren, etc.)
            if (_dragNode.IsContainer)
            {
                double dx = newX - _dragNode.X;
                double dy = newY - _dragNode.Y;
                MoveDescendants(_dragNode, dx, dy);
            }

            _dragNode.X = newX;
            _dragNode.Y = newY;
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
        var hoveredChevron = HitChevron(canvasPos);
        Cursor = hoveredPort != null ? new Cursor(StandardCursorType.Hand)
            : hoveredChevron != null ? new Cursor(StandardCursorType.Hand)
            : hoveredResize != null ? new Cursor(StandardCursorType.BottomRightCorner)
            : Cursor.Default;
    }

    // ── Input: Pointer Release ─────────────────────────────────

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm == null) return;

        // Complete wire connection (new draw, or reroute commit / cancel).
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

            // Did the intended wire actually land? (AddConnection silently rejects
            // incompatible kinds/types and dups — a successful commit leaves the
            // pair present, even if 1:N replacement kept Connections.Count flat.)
            bool committed = targetPort != null && _vm.Connections.Any(c =>
                   (c.Source == _wireStartPort && c.Target == targetPort)
                || (c.Source == targetPort && c.Target == _wireStartPort));

            if (!committed)
            {
                if (_reroutingOriginal != null)
                {
                    // Reroute drop-off: restore the original wire (existing behavior).
                    _vm.Connections.Add(_reroutingOriginal);
                }
                else
                {
                    // Fresh wire drag released on empty space → open the quick-add
                    // popup anchored at the cursor with the source pin captured.
                    // Auto-wire happens in CommitQuickAdd if the user picks a result.
                    var localPos = e.GetPosition(this);
                    var windowPos = this.GetVisualRoot() is Visual root
                        ? (TranslatePoint(localPos, root) ?? localPos)
                        : localPos;
                    OpenQuickAddAt(windowPos, _wireStartPort);
                }
            }
        }

        // Snap dropped node into container zone (only if it actually moved)
        if (_isDraggingNode && _dragNode != null)
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
        _reroutingOriginal = null;
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
        var prevContainer = node.ParentContainer;
        var prevZone = node.ParentZone;
        bool hadPrevZone = prevZone != null;

        // Remove from previous zone
        if (node.ParentContainer != null && node.ParentZone != null)
        {
            node.ParentZone.Children.Remove(node);
            node.ParentContainer = null;
            node.ParentZone = null;
        }

        double nodeCenterX = node.X + node.EffectiveWidth / 2;
        double nodeCenterY = node.Y + node.Height / 2;

        // Collect all matching (container, zone) pairs, then pick the deepest
        (GraphNode container, ContainerZone zone)? bestMatch = null;
        int bestDepth = -1;

        foreach (var container in _vm.Nodes.Where(n => n.IsContainer))
        {
            // Skip self-nesting
            if (container == node) continue;

            // Skip if target container is a descendant of the node being dropped (would create a cycle)
            if (node.IsContainer && IsDescendantOf(container, node)) continue;

            foreach (var zone in container.Zones)
            {
                var (zx, zy, zw, zh) = zone.GetAbsoluteRect(container);
                if (nodeCenterX >= zx && nodeCenterX <= zx + zw &&
                    nodeCenterY >= zy && nodeCenterY <= zy + zh)
                {
                    int depth = GetNestingDepth(container);
                    if (depth > bestDepth)
                    {
                        bestDepth = depth;
                        bestMatch = (container, zone);
                    }
                }
            }
        }

        if (bestMatch != null)
        {
            var (targetContainer, targetZone) = bestMatch.Value;
            var (zx, zy, zw, zh) = targetZone.GetAbsoluteRect(targetContainer);

            node.ParentContainer = targetContainer;
            node.ParentZone = targetZone;

            if (!targetZone.Children.Contains(node))
                targetZone.Children.Add(node);

            double pad = GraphNode.ZonePadding;

            // Smart default: if this node had no previous zone (freshly created/palette drop),
            // use stacking logic to find the next available position
            if (!hadPrevZone)
            {
                double snapTop = zy + GraphNode.ZoneHeaderHeight + pad;
                int childIndex = targetZone.Children.IndexOf(node);
                for (int i = 0; i < childIndex; i++)
                    snapTop += targetZone.Children[i].Height + pad;

                node.X = Math.Round((zx + pad) / 10) * 10;
                node.Y = Math.Round(snapTop / 10) * 10;
            }
            else
            {
                // Free placement: clamp position so node stays within zone bounds
                double minX = zx + pad;
                double minY = zy + GraphNode.ZoneHeaderHeight + pad;
                node.X = Math.Round(Math.Max(minX, node.X) / 10) * 10;
                node.Y = Math.Round(Math.Max(minY, node.Y) / 10) * 10;
            }

            // Auto-grow the container if the node extends beyond
            targetContainer.AutoGrowToFitChildren();
            return;
        }

        // Dropped outside any zone — shrink the previous container if there was one
        if (prevContainer != null)
            prevContainer.ShrinkToFitChildren();
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
            double nw = NodeLayout.GetEffectiveWidth(n);
            if (canvasPos.X >= n.X && canvasPos.X <= n.X + nw &&
                canvasPos.Y >= n.Y && canvasPos.Y <= n.Y + n.Height)
                return n;
        }

        // Container headers: check deepest first so clicking a nested container selects it
        foreach (var n in _vm.Nodes
            .Where(n => n.IsContainer)
            .OrderByDescending(n => GetNestingDepth(n)))
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
                // Hidden pins on collapsed nodes mustn't accept clicks.
                if (!node.IsPortVisible(port)) continue;

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

    /// <summary>
    /// Hit-test for the collapse chevron in a regular node's header — a 24×HeaderHeight
    /// box anchored at the top-right corner. Containers use the Function zone model
    /// instead and don't carry a chevron.
    /// </summary>
    private GraphNode? HitChevron(Point canvasPos)
    {
        if (_vm == null) return null;

        double hh = GraphNode.HeaderHeight;
        for (int i = _vm.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _vm.Nodes[i];
            if (n.IsContainer) continue;

            double w = NodeLayout.GetEffectiveWidth(n);
            double cx = n.X + w - 24;
            if (canvasPos.X >= cx && canvasPos.X <= n.X + w &&
                canvasPos.Y >= n.Y && canvasPos.Y <= n.Y + hh)
                return n;
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

    // ── Nesting helpers ────────────────────────────────────────

    private static bool IsDescendantOf(GraphNode candidate, GraphNode ancestor)
    {
        var current = candidate.ParentContainer;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.ParentContainer;
        }
        return false;
    }

    private static int GetNestingDepth(GraphNode node)
    {
        int depth = 0;
        var current = node.ParentContainer;
        while (current != null) { depth++; current = current.ParentContainer; }
        return depth;
    }

    private static void MoveDescendants(GraphNode container, double dx, double dy)
    {
        foreach (var zone in container.Zones)
            foreach (var child in zone.Children)
            {
                child.X += dx;
                child.Y += dy;
                if (child.IsContainer)
                    MoveDescendants(child, dx, dy);
            }
    }

    // ── Public commands (driven by keyboard shortcuts) ─────────

    /// <summary>
    /// Fit the viewport to contain all top-level nodes (or just the selected
    /// node when <paramref name="selectionOnly"/> is true). No-op if the graph
    /// is empty or the control has no usable size yet.
    /// </summary>
    public void ZoomToFit(bool selectionOnly = false)
    {
        if (_vm == null) return;
        if (Bounds.Width < 50 || Bounds.Height < 50) return;

        var set = (selectionOnly && _vm.SelectedNode != null
            ? new[] { _vm.SelectedNode }
            : _vm.Nodes.Where(n => n.ParentContainer == null).ToArray());
        if (set.Length == 0) return;

        double minX = set.Min(n => n.X);
        double minY = set.Min(n => n.Y);
        double maxX = set.Max(n => n.X + (n.IsContainer ? n.ContainerWidth : NodeLayout.GetEffectiveWidth(n)));
        double maxY = set.Max(n => n.Y + (n.IsContainer ? n.ContainerHeight : n.Height));

        const double pad = 80;
        double w = (maxX - minX) + pad * 2;
        double h = (maxY - minY) + pad * 2;

        double zoom = Math.Min(Bounds.Width / w, Bounds.Height / h);
        zoom = Math.Clamp(zoom, 0.1, 3.0);

        // Center the bounding rect in the viewport at the new zoom.
        double cx = (minX + maxX) / 2;
        double cy = (minY + maxY) / 2;
        _vm.Zoom = zoom;
        _vm.PanX = Bounds.Width / 2 - cx * zoom;
        _vm.PanY = Bounds.Height / 2 - cy * zoom;
    }

    /// <summary>
    /// Cancel any in-flight drag: wire drag (restoring a rerouted wire if
    /// applicable), node drag, container resize, or panning. Used by Esc.
    /// </summary>
    public void CancelDrag()
    {
        if (_isDraggingWire && _reroutingOriginal != null && _vm != null)
            _vm.Connections.Add(_reroutingOriginal);

        _isPanning = false;
        _isDraggingNode = false;
        _dragNode = null;
        _isDraggingWire = false;
        _wireStartPort = null;
        _reroutingOriginal = null;
        _isResizingContainer = false;
        _resizeNode = null;
    }

    // ── Rendering ──────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_vm == null) return;

        _renderer.Render(context, Bounds, _vm,
            _isDraggingWire, _wireStartPort, _wireEndPoint,
            _isDraggingNode, _dragNode);
    }
}
