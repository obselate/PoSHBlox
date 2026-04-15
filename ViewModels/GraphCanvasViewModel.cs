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
    [ObservableProperty] private GraphNode? _selectedNode;

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
        if (SelectedNode == null) return;

        // Collect all nodes to delete (including descendants if container)
        var toDelete = new List<GraphNode> { SelectedNode };
        if (SelectedNode.IsContainer)
            CollectDescendants(SelectedNode, toDelete);

        foreach (var node in toDelete)
        {
            // Remove connections
            var conns = Connections
                .Where(c => c.Source.Owner == node || c.Target.Owner == node)
                .ToList();
            foreach (var c in conns)
                Connections.Remove(c);

            // Remove from parent zone
            if (node.ParentZone != null)
                node.ParentZone.Children.Remove(node);

            Nodes.Remove(node);
        }

        SelectedNode = null;
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
    /// Deep-copy the selected non-container node at a 40px offset and select
    /// the duplicate. Ports/parameters get fresh IDs; wires are not carried
    /// over (duplicate means "start a copy", not "mirror with shared I/O").
    /// Containers aren't supported in this pass — duplicating a container's
    /// children and rewiring internal connections is a larger task.
    /// </summary>
    [RelayCommand]
    public void DuplicateSelected()
    {
        if (SelectedNode == null || SelectedNode.IsContainer) return;

        var src = SelectedNode;
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

        Nodes.Add(dup);
        SelectNode(dup);
    }

    /// <summary>Toggle the selected node's collapsed state — hides non-essential params.</summary>
    [RelayCommand]
    public void ToggleCollapseSelected()
    {
        if (SelectedNode == null || SelectedNode.IsContainer) return;
        SelectedNode.IsCollapsed = !SelectedNode.IsCollapsed;
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

    public void SelectNode(GraphNode? node)
    {
        if (SelectedNode != null) SelectedNode.IsSelected = false;
        SelectedNode = node;
        if (node != null) node.IsSelected = true;
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
