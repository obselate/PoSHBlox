using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoSHBlox.Models;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

public partial class GraphCanvasViewModel : ObservableObject
{
    // ── Graph state ────────────────────────────────────────────
    public ObservableCollection<GraphNode> Nodes { get; } = new();
    public ObservableCollection<NodeConnection> Connections { get; } = new();

    // ── Sub-ViewModels ─────────────────────────────────────────
    public NodePaletteViewModel Palette { get; } = new();

    // ── Canvas transform ───────────────────────────────────────
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;
    [ObservableProperty] private double _zoom = 1.0;

    // ── Selection ──────────────────────────────────────────────
    [ObservableProperty] private GraphNode? _selectedNode;

    // ── Panel visibility ─────────────────────────────────────
    [ObservableProperty] private bool _isPaletteOpen = true;
    [ObservableProperty] private bool _isPreviewOpen;

    // ── Project state ─────────────────────────────────────────
    [ObservableProperty] private string? _currentFilePath;
    private DateTime? _projectCreatedUtc;

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
        SeedExampleGraph();
    }

    // ── Commands ────────────────────────────────────────────────

    [RelayCommand]
    public void NewGraph()
    {
        Connections.Clear();
        Nodes.Clear();
        SelectedNode = null;
        CurrentFilePath = null;
        _projectCreatedUtc = null;
        _spawnCount = 0;
        PanX = 0;
        PanY = 0;
        Zoom = 1.0;
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
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedNode == null) return;

        // Remove all connections to/from this node
        var toRemove = Connections
            .Where(c => c.Source.Owner == SelectedNode || c.Target.Owner == SelectedNode)
            .ToList();
        foreach (var c in toRemove)
            Connections.Remove(c);

        // Remove from parent zone if nested
        if (SelectedNode.ParentZone != null)
            SelectedNode.ParentZone.Children.Remove(SelectedNode);

        Nodes.Remove(SelectedNode);
        SelectedNode = null;
    }

    [RelayCommand]
    public void ResetView()
    {
        PanX = 0;
        PanY = 0;
        Zoom = 1.0;
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
        if (source.Owner == target.Owner) return;
        if (Connections.Any(c => c.Source == source && c.Target == target)) return;
        if (source.Direction != PortDirection.Output || target.Direction != PortDirection.Input) return;

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
        _projectCreatedUtc = doc.Metadata.CreatedUtc;
        ProjectSerializer.RebuildGraph(doc, this);
    }

    // ── Example graph seed ─────────────────────────────────────

    private void SeedExampleGraph()
    {
        var getProcess = new GraphNode
        {
            Title = "Get-Process",
            Category = "Process / Service",
            CmdletName = "Get-Process",
            X = 100, Y = 150,
        };
        getProcess.Inputs.Clear();
        getProcess.Parameters.Add(new NodeParameter
        {
            Name = "Name",
            Type = ParamType.String,
            Description = "Process name filter",
        });

        var sort = new GraphNode
        {
            Title = "Sort-Object",
            Category = "String / Data",
            CmdletName = "Sort-Object",
            X = 400, Y = 150,
        };
        sort.Parameters.Add(new NodeParameter
        {
            Name = "Property", Type = ParamType.String,
            IsMandatory = true, Value = "CPU",
            Description = "Property to sort by",
        });
        sort.Parameters.Add(new NodeParameter
        {
            Name = "Descending", Type = ParamType.Bool,
            Value = "true", Description = "Sort descending",
        });

        Nodes.Add(getProcess);
        Nodes.Add(sort);

        AddConnection(getProcess.Outputs[0], sort.Inputs[0]);
    }
}
