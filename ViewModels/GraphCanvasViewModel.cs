using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoSHBlox.Models;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

public partial class GraphCanvasViewModel : ObservableObject
{
    // ── Static enum options for XAML binding ─────────────────────
    public static ParamType[] ArgumentTypeOptions { get; } =
    [
        ParamType.String,
        ParamType.Int,
        ParamType.Bool,
        ParamType.StringArray,
        ParamType.ScriptBlock,
    ];

    // ── Graph state ────────────────────────────────────────────
    public ObservableCollection<GraphNode> Nodes { get; } = new();
    public ObservableCollection<NodeConnection> Connections { get; } = new();

    // ── Sub-ViewModels ─────────────────────────────────────────
    public NodePaletteViewModel Palette { get; } = new();
    public QuickAddPopupViewModel QuickAdd { get; }

    // ── Canvas transform ───────────────────────────────────────
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;
    [ObservableProperty] private double _zoom = 1.0;

    // ── Selection ──────────────────────────────────────────────
    /// <summary>
    /// The primary (most-recently-added) selected node — what the properties
    /// panel binds to. Stays present even in multi-select so existing
    /// single-node bindings keep working.
    /// </summary>
    [ObservableProperty] private GraphNode? _selectedNode;

    /// <summary>
    /// All nodes currently selected. Invariant: if any node is selected,
    /// <see cref="SelectedNode"/> is one of them. Editing commands (Delete,
    /// Duplicate, Collapse) iterate this collection.
    /// </summary>
    public ObservableCollection<GraphNode> SelectedNodes { get; } = new();

    public bool HasMultiSelection => SelectedNodes.Count > 1;
    public int SelectionCount => SelectedNodes.Count;

    // ── Panel visibility ─────────────────────────────────────
    [ObservableProperty] private bool _isPaletteOpen = true;
    [ObservableProperty] private bool _isPreviewOpen;

    /// <summary>Toggled by the '?' shortcut — overlays a keyboard cheat sheet.</summary>
    [ObservableProperty] private bool _isCheatSheetOpen;

    // ── Project state ─────────────────────────────────────────
    [ObservableProperty] private string? _currentFilePath;
    [ObservableProperty] private bool _isDirty;
    private DateTime? _projectCreatedUtc;
    private bool _suppressDirty;

    public string WindowTitle => CurrentFilePath != null
        ? $"PoSHBlox \u2014 {Path.GetFileName(CurrentFilePath)}"
        : "PoSHBlox \u2014 PowerShell Visual Scripting";

    partial void OnCurrentFilePathChanged(string? value) => OnPropertyChanged(nameof(WindowTitle));

    // ── Pending connection state (exposed for canvas) ──────────
    [ObservableProperty] private bool _isDraggingConnection;
    [ObservableProperty] private double _pendingWireStartX;
    [ObservableProperty] private double _pendingWireStartY;
    [ObservableProperty] private double _pendingWireEndX;
    [ObservableProperty] private double _pendingWireEndY;
    public NodePort? PendingSourcePort { get; set; }

    // ── Spawn offset counter ───────────────────────────────────
    private int _spawnCount;

    public GraphCanvasViewModel()
    {
        QuickAdd = new QuickAddPopupViewModel(Palette);
        Palette.SetGraphNodes(Nodes);
        Nodes.CollectionChanged += OnNodesChanged;
        Connections.CollectionChanged += (_, _) =>
        {
            MarkDirty();
            RefreshWiredState();
            RefreshValidation();
        };

        SeedExampleGraph();
        RefreshWiredState();
        RefreshParameterSetVisibility();
        RefreshValidation();
        IsDirty = false;
    }

    /// <summary>
    /// Populate <see cref="GraphNode.Issues"/> across all nodes from the current
    /// graph state. Called on the same change hooks that drive RefreshWiredState
    /// — connection changes, param-value changes, active-set flips, node/param
    /// collection mutations. Validator runs once per refresh over the whole
    /// graph; cheap enough for typical sizes.
    /// </summary>
    private void RefreshValidation()
    {
        var issues = GraphValidator.Validate(Nodes, Connections);
        foreach (var node in Nodes)
        {
            node.Issues.Clear();
            if (issues.TryGetValue(node.Id, out var list))
                foreach (var i in list) node.Issues.Add(i);
        }
    }

    /// <summary>
    /// Sync <see cref="NodeParameter.IsInActiveSet"/> on every parameter so the
    /// properties panel hides rows outside the active set. No-op for nodes whose
    /// cmdlet doesn't declare sets (KnownParameterSets empty).
    /// </summary>
    private void RefreshParameterSetVisibility()
    {
        foreach (var node in Nodes)
        {
            bool nodeHasSets = node.KnownParameterSets.Length > 0;
            foreach (var p in node.Parameters)
            {
                // Common params (no declared sets) always in scope. Otherwise the
                // active set must appear in the param's sets list.
                bool inScope = !nodeHasSets
                            || p.ParameterSets.Length == 0
                            || p.ParameterSets.Contains(node.ActiveParameterSet,
                                   StringComparer.OrdinalIgnoreCase);
                p.IsInActiveSet = inScope;

                // Mandatory-per-set: when MandatoryInSets has entries, the param
                // is mandatory iff the active set is in the list. Fall back to
                // the flat IsMandatory when no per-set data is present.
                p.IsEffectivelyMandatory = p.MandatoryInSets.Length > 0
                    ? p.MandatoryInSets.Contains(node.ActiveParameterSet, StringComparer.OrdinalIgnoreCase)
                    : p.IsMandatory;
            }
        }
    }

    /// <summary>
    /// Recompute <see cref="NodeParameter.IsWired"/> and its label for every
    /// parameter across every node. Called on connection, param, and node
    /// collection changes — cheap enough for typical graph sizes.
    /// </summary>
    private void RefreshWiredState()
    {
        foreach (var node in Nodes)
        {
            foreach (var param in node.Parameters)
            {
                var pin = param.InputPort;
                if (pin == null)
                {
                    param.IsWired = false;
                    param.WiredFromLabel = "";
                    continue;
                }
                var conn = Connections.FirstOrDefault(c => c.Target == pin);
                if (conn == null)
                {
                    param.IsWired = false;
                    param.WiredFromLabel = "";
                }
                else
                {
                    var src = conn.Source.Owner;
                    var srcTitle = string.IsNullOrWhiteSpace(src?.Title) ? "?" : src!.Title;
                    var pinName  = string.IsNullOrWhiteSpace(conn.Source.Name)
                        ? (conn.Source.IsPrimary ? "Out" : "Pin")
                        : conn.Source.Name;
                    param.IsWired = true;
                    param.WiredFromLabel = $"\u2190 {srcTitle}.{pinName}";
                }
            }
        }
    }

    // ── Commands ────────────────────────────────────────────────

    [RelayCommand]
    public void NewGraph()
    {
        _suppressDirty = true;
        Connections.Clear();
        Nodes.Clear();
        SelectedNode = null;
        CurrentFilePath = null;
        _projectCreatedUtc = null;
        _spawnCount = 0;
        PanX = 0;
        PanY = 0;
        Zoom = 1.0;
        _suppressDirty = false;
        IsDirty = false;
    }

    [RelayCommand]
    public void AddNode()
    {
        var node = NodeFactory.CreateBlank(
            -PanX / Zoom + 300,
            -PanY / Zoom + 300);
        Nodes.Add(node);
        SelectNode(node);
    }

    [RelayCommand]
    public void SpawnFromTemplate(NodeTemplate? template)
    {
        if (template == null) return;

        var offset = (_spawnCount++ % 8) * 30;
        double x = -PanX / Zoom + 200 + offset;
        double y = -PanY / Zoom + 150 + offset;

        var node = NodeFactory.CreateFromTemplate(template, x, y);
        Nodes.Add(node);
        SelectNode(node);
        Palette.NoteSpawn(template);
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedNodes.Count == 0) return;

        // Collect every node to delete — selected nodes plus every descendant
        // of any selected container. Deduplicate via a set so a child that
        // belongs to a selected container isn't processed twice.
        var toDelete = new HashSet<GraphNode>();
        foreach (var sel in SelectedNodes.ToList())
        {
            toDelete.Add(sel);
            if (sel.IsContainer)
            {
                var kids = new List<GraphNode>();
                CollectDescendants(sel, kids);
                foreach (var k in kids) toDelete.Add(k);
            }
        }

        foreach (var node in toDelete)
        {
            // Remove connections touching this node.
            var conns = Connections
                .Where(c => c.Source.Owner == node || c.Target.Owner == node)
                .ToList();
            foreach (var c in conns)
                Connections.Remove(c);

            if (node.ParentZone != null)
                node.ParentZone.Children.Remove(node);

            Nodes.Remove(node);
        }

        ClearSelection();
    }

    private static void CollectDescendants(GraphNode container, List<GraphNode> result)
    {
        foreach (var zone in container.Zones)
            foreach (var child in zone.Children)
            {
                result.Add(child);
                if (child.IsContainer)
                    CollectDescendants(child, result);
            }
    }

    [RelayCommand]
    public void ResetView()
    {
        PanX = 0;
        PanY = 0;
        Zoom = 1.0;
    }

    /// <summary>
    /// Deep-copy every selected non-container node at a 40px offset and select
    /// the duplicates. Ports/parameters get fresh IDs; wires are not carried
    /// over — duplicate is "start a copy", not "mirror with shared I/O".
    /// Containers are skipped (zone-subtree duplication is a bigger task).
    /// </summary>
    [RelayCommand]
    public void DuplicateSelected()
    {
        if (SelectedNodes.Count == 0) return;

        var sources = SelectedNodes.Where(n => !n.IsContainer).ToList();
        if (sources.Count == 0) return;

        // Clone first so we don't mutate SelectedNodes while iterating when we
        // retarget the selection to the new dupes.
        var dupes = sources.Select(CloneNode).ToList();

        ClearSelection();
        foreach (var dup in dupes)
        {
            Nodes.Add(dup);
            AddToSelection(dup);
        }
    }

    /// <summary>
    /// Toggle collapsed state on every selected non-container node. When a mix
    /// is currently selected, the first node's current state drives the target:
    /// "make them all match the opposite of the primary".
    /// </summary>
    [RelayCommand]
    public void ToggleCollapseSelected()
    {
        var targets = SelectedNodes.Where(n => !n.IsContainer).ToList();
        if (targets.Count == 0) return;

        bool newState = !targets[0].IsCollapsed;
        foreach (var n in targets)
            n.IsCollapsed = newState;
    }

    private GraphNode CloneNode(GraphNode src)
    {
        var dup = new GraphNode
        {
            Title = src.Title,
            Category = src.Category,
            CmdletName = src.CmdletName,
            ScriptBody = src.ScriptBody,
            // Reset OutputVariable so the duplicate gets a fresh auto-name.
            OutputVariable = "",
            X = src.X + 40,
            Y = src.Y + 40,
            Width = src.Width,
            IsCollapsed = src.IsCollapsed,
            KnownParameterSets = (string[])src.KnownParameterSets.Clone(),
            ActiveParameterSet = src.ActiveParameterSet,
        };

        // Default constructor seeds with ExecIn/ExecOut/Out — replace with a
        // faithful copy of the source's pin shape so we preserve per-parameter
        // pairing and any custom outputs.
        dup.Inputs.Clear();
        dup.Outputs.Clear();
        foreach (var p in src.Inputs)
            dup.Inputs.Add(ClonePort(p, dup));
        foreach (var p in src.Outputs)
            dup.Outputs.Add(ClonePort(p, dup));

        foreach (var param in src.Parameters)
            dup.Parameters.Add(new NodeParameter
            {
                Name = param.Name,
                Type = param.Type,
                IsMandatory = param.IsMandatory,
                DefaultValue = param.DefaultValue,
                Description = param.Description,
                ValidValues = param.ValidValues,
                Value = param.Value,
                IsArgument = param.IsArgument,
                IsPipelineInput = param.IsPipelineInput,
                ParameterSets = (string[])param.ParameterSets.Clone(),
                MandatoryInSets = (string[])param.MandatoryInSets.Clone(),
                Owner = dup,
            });

        return dup;
    }

    /// <summary>
    /// Spawn a template from the quick-add popup at the recorded cursor position,
    /// auto-wiring from the pending source pin when present. Used by the popup
    /// Commit action and by the pin-drag-on-empty-space trigger.
    /// </summary>
    public void CommitQuickAdd(NodeTemplate template)
    {
        if (template == null) return;
        var x = QuickAdd.X;
        var y = QuickAdd.Y;
        var source = QuickAdd.SourcePort;

        var node = NodeFactory.CreateFromTemplate(template, x, y);
        Nodes.Add(node);
        SelectNode(node);
        Palette.NoteSpawn(template);

        if (source != null) AutoWire(source, node);

        QuickAdd.Close();
    }

    /// <summary>
    /// Connect a fresh <paramref name="node"/> to the pending wire source on
    /// its most sensible pin. Exec-from-output ↔ target's ExecIn; data-from-
    /// output ↔ PrimaryPipelineTarget (or first compatible data input); also
    /// best-effort chain the exec wire when both ends have one. Input-side
    /// drags (user grabbed an input pin) go in the opposite direction.
    /// </summary>
    private void AutoWire(NodePort source, GraphNode node)
    {
        if (source.Direction == PortDirection.Output)
        {
            if (source.Kind == PortKind.Exec)
            {
                if (node.ExecInPort != null) AddConnection(source, node.ExecInPort);
                return;
            }
            // Data output → primary pipeline target preferred, fall back to
            // the first compatible data input.
            var tgt = node.PrimaryPipelineTarget
                   ?? node.DataInputs.FirstOrDefault(p => PortCompatibility.CanConnect(source, p));
            if (tgt != null) AddConnection(source, tgt);

            // Chain exec too if both sides have exec pins — keeps pipeline
            // collapse eligible when the user later wants it.
            if (source.Owner?.ExecOutPort is { } srcExecOut && node.ExecInPort != null)
                AddConnection(srcExecOut, node.ExecInPort);
            return;
        }

        // Input-direction drag — user grabbed an input pin looking for a producer.
        if (source.Kind == PortKind.Exec)
        {
            if (node.ExecOutPort != null) AddConnection(node.ExecOutPort, source);
            return;
        }
        var producer = node.PrimaryDataOutput
                    ?? node.DataOutputs.FirstOrDefault(p => PortCompatibility.CanConnect(p, source));
        if (producer != null) AddConnection(producer, source);
    }

    private static NodePort ClonePort(NodePort p, GraphNode owner) => new()
    {
        Name = p.Name,
        Direction = p.Direction,
        Kind = p.Kind,
        DataType = p.DataType,
        ParameterName = p.ParameterName,
        IsPrimary = p.IsPrimary,
        IsPrimaryPipelineTarget = p.IsPrimaryPipelineTarget,
        Owner = owner,
    };

    [RelayCommand]
    public void AddArgument()
    {
        if (SelectedNode is not { ContainerType: ContainerType.Function }) return;
        SelectedNode.Parameters.Add(new NodeParameter
        {
            Name = "NewParam",
            Type = ParamType.String,
            IsArgument = true,
            Description = "Function argument",
        });
    }

    [RelayCommand]
    public void RemoveArgument(NodeParameter arg)
    {
        if (SelectedNode is not { ContainerType: ContainerType.Function }) return;
        if (arg.IsArgument)
            SelectedNode.Parameters.Remove(arg);
    }

    // ── Public helpers ─────────────────────────────────────────

    /// <summary>
    /// Replace the entire selection with just <paramref name="node"/>
    /// (or clear if null). Primary interaction for plain clicks.
    /// </summary>
    public void SelectNode(GraphNode? node)
    {
        ClearSelection();
        if (node != null) AddToSelection(node);
    }

    /// <summary>Add a node to the selection, updating primary to point at it.</summary>
    public void AddToSelection(GraphNode node)
    {
        if (!SelectedNodes.Contains(node))
        {
            SelectedNodes.Add(node);
            node.IsSelected = true;
        }
        SelectedNode = node;
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectionCount));
    }

    /// <summary>Remove a node from the selection.</summary>
    public void RemoveFromSelection(GraphNode node)
    {
        if (SelectedNodes.Remove(node))
            node.IsSelected = false;
        if (SelectedNode == node)
            SelectedNode = SelectedNodes.LastOrDefault();
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectionCount));
    }

    /// <summary>Toggle a node's selection membership. Used by shift-click.</summary>
    public void ToggleSelection(GraphNode node)
    {
        if (SelectedNodes.Contains(node))
            RemoveFromSelection(node);
        else
            AddToSelection(node);
    }

    /// <summary>Clear all selection state.</summary>
    public void ClearSelection()
    {
        foreach (var n in SelectedNodes) n.IsSelected = false;
        SelectedNodes.Clear();
        SelectedNode = null;
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectionCount));
    }

    /// <summary>
    /// Replace (or extend, when <paramref name="extend"/> is true) the selection
    /// with every top-level node whose bounds intersect the given canvas-space
    /// rectangle. Used by the lasso drag on empty canvas.
    /// </summary>
    public void SelectNodesInRect(double x, double y, double width, double height, bool extend = false)
    {
        if (!extend) ClearSelection();
        double x2 = x + width, y2 = y + height;
        foreach (var n in Nodes)
        {
            if (n.ParentContainer != null) continue;   // skip zone-nested
            double nw = n.IsContainer ? n.ContainerWidth : n.Width;
            double nh = n.IsContainer ? n.ContainerHeight : n.Height;
            double nx2 = n.X + nw, ny2 = n.Y + nh;
            bool intersects = !(nx2 < x || n.X > x2 || ny2 < y || n.Y > y2);
            if (intersects && !SelectedNodes.Contains(n))
                AddToSelection(n);
        }
    }

    public void AddConnection(NodePort source, NodePort target)
    {
        // Direction-specific guard (the shared compatibility check is direction-
        // agnostic; here we enforce the canonical Output→Input ordering).
        if (source.Direction != PortDirection.Output || target.Direction != PortDirection.Input) return;
        if (!PortCompatibility.CanConnect(source, target)) return;

        if (source.Kind == PortKind.Data)
        {
            // Data input accepts exactly one upstream. Replace any existing wire.
            foreach (var c in Connections.Where(c => c.Target == target).ToList())
                Connections.Remove(c);
        }
        else // PortKind.Exec
        {
            // ExecOut fires exactly one successor. ExecIn accepts many (N→1 merge).
            foreach (var c in Connections.Where(c => c.Source == source).ToList())
                Connections.Remove(c);
        }

        if (Connections.Any(c => c.Source == source && c.Target == target)) return;

        Connections.Add(new NodeConnection { Source = source, Target = target });
    }

    public void RemoveConnectionsForPort(NodePort port)
    {
        var toRemove = Connections.Where(c => c.Source == port || c.Target == port).ToList();
        foreach (var c in toRemove)
            Connections.Remove(c);
    }

    public void ClampZoom()
    {
        Zoom = Math.Clamp(Zoom, 0.1, 3.0);
    }

    // ── Project load ───────────────────────────────────────────

    public DateTime? ProjectCreatedUtc => _projectCreatedUtc;

    public void LoadFromDocument(PblxDocument doc)
    {
        _suppressDirty = true;
        _projectCreatedUtc = doc.Metadata.CreatedUtc;
        ProjectSerializer.RebuildGraph(doc, this);
        _suppressDirty = false;
        RefreshWiredState();
        RefreshParameterSetVisibility();
        RefreshValidation();
        IsDirty = false;
        Palette.SyncFunctionTemplates();
    }

    // ── Dirty tracking ────────────────────────────────────────

    private void MarkDirty()
    {
        if (!_suppressDirty) IsDirty = true;
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();

        if (e.NewItems != null)
            foreach (GraphNode node in e.NewItems)
                SubscribeNode(node);

        if (e.OldItems != null)
            foreach (GraphNode node in e.OldItems)
                UnsubscribeNode(node);

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Clear was called — can't enumerate old items, they're gone.
            // Nodes list is now empty so nothing to unsubscribe.
        }

        // Fresh nodes need their params' IsInActiveSet / IsEffectivelyMandatory
        // computed, and the graph needs revalidating (a freshly-spawned node is
        // usually an orphan + has missing mandatory params until wired up).
        RefreshWiredState();
        RefreshParameterSetVisibility();
        RefreshValidation();
        Palette.SyncFunctionTemplates();
    }

    private static readonly string[] IgnoredNodeProps = [nameof(GraphNode.IsSelected)];

    private void SubscribeNode(GraphNode node)
    {
        node.PropertyChanged += OnNodePropertyChanged;
        foreach (var p in node.Parameters)
            p.PropertyChanged += OnParamPropertyChanged;
        node.Parameters.CollectionChanged += OnNodeParamsChanged;
    }

    private void UnsubscribeNode(GraphNode node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        foreach (var p in node.Parameters)
            p.PropertyChanged -= OnParamPropertyChanged;
        node.Parameters.CollectionChanged -= OnNodeParamsChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && !IgnoredNodeProps.Contains(e.PropertyName))
            MarkDirty();

        // A title rename changes the upstream label shown on any wired downstream
        // parameter — refresh so the chip stays accurate.
        if (e.PropertyName == nameof(GraphNode.Title))
            RefreshWiredState();

        // Active-set switch changes which params render + which are mandatory.
        if (e.PropertyName == nameof(GraphNode.ActiveParameterSet))
        {
            RefreshParameterSetVisibility();
            RefreshValidation();
        }
    }

    private void OnParamPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeParameter.Value))
        {
            MarkDirty();
            Palette.SyncFunctionTemplates();
            // Filling in a mandatory param's value clears its issue. Re-validate.
            RefreshValidation();
        }
    }

    private void OnNodeParamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();

        if (e.NewItems != null)
            foreach (NodeParameter p in e.NewItems)
                p.PropertyChanged += OnParamPropertyChanged;

        if (e.OldItems != null)
            foreach (NodeParameter p in e.OldItems)
                p.PropertyChanged -= OnParamPropertyChanged;

        RefreshWiredState();
        RefreshParameterSetVisibility();
        RefreshValidation();
        Palette.SyncFunctionTemplates();
    }

    // ── Example graph seed ─────────────────────────────────────

    private void SeedExampleGraph()
    {
        var getProcess = NodeFactory.CreateFromTemplate(new NodeTemplate
        {
            Name = "Get-Process",
            Category = "Process / Service",
            CmdletName = "Get-Process",
            HasExecIn = false,
            DataOutputs =
            [
                new DataOutputDef { Name = "Processes", Type = ParamType.Collection, IsPrimary = true },
            ],
            Parameters =
            [
                new ParameterDef { Name = "Name", Type = ParamType.String, Description = "Process name filter" },
            ],
        }, x: 100, y: 150);

        var sort = NodeFactory.CreateFromTemplate(new NodeTemplate
        {
            Name = "Sort-Object",
            Category = "String / Data",
            CmdletName = "Sort-Object",
            PrimaryPipelineParameter = "InputObject",
            DataOutputs =
            [
                new DataOutputDef { Name = "Sorted", Type = ParamType.Collection, IsPrimary = true },
            ],
            Parameters =
            [
                new ParameterDef { Name = "InputObject", Type = ParamType.Collection, IsPipelineInput = true,
                                   Description = "Upstream data via pipeline" },
                new ParameterDef { Name = "Property", Type = ParamType.String, IsMandatory = true,
                                   DefaultValue = "CPU", Description = "Property to sort by" },
                new ParameterDef { Name = "Descending", Type = ParamType.Bool, DefaultValue = "true",
                                   Description = "Sort descending" },
            ],
        }, x: 400, y: 150);

        Nodes.Add(getProcess);
        Nodes.Add(sort);

        // Wire the exec flow and the primary data pipe.
        if (getProcess.ExecOutPort != null && sort.ExecInPort != null)
            AddConnection(getProcess.ExecOutPort, sort.ExecInPort);
        if (getProcess.PrimaryDataOutput != null && sort.PrimaryPipelineTarget != null)
            AddConnection(getProcess.PrimaryDataOutput, sort.PrimaryPipelineTarget);
    }
}
