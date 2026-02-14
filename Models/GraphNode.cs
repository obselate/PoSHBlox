using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoSHBlox.Models;

public partial class GraphNode : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    // ── Core identity ──────────────────────────────────────────
    [ObservableProperty] private string _title = "New Node";
    [ObservableProperty] private string _category = "Custom";
    [ObservableProperty] private string _scriptBody = "# Your script here";
    public string CmdletName { get; set; } = "";
    public bool IsCmdletNode => !string.IsNullOrEmpty(CmdletName);

    /// <summary>
    /// User-defined output variable name. If set, the node's output is stored as $ThisName.
    /// If blank, auto-generated from Title + short ID when a variable is needed.
    /// </summary>
    [ObservableProperty] private string _outputVariable = "";

    // ── Position & sizing ──────────────────────────────────────
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 200;
    [ObservableProperty] private bool _isSelected;

    // ── Ports & parameters ─────────────────────────────────────
    public ObservableCollection<NodePort> Inputs { get; } = new();
    public ObservableCollection<NodePort> Outputs { get; } = new();
    public ObservableCollection<NodeParameter> Parameters { get; } = new();

    // ── Container support ──────────────────────────────────────
    public ContainerType ContainerType { get; set; } = ContainerType.None;
    public bool IsContainer => ContainerType != ContainerType.None;
    public List<ContainerZone> Zones { get; } = new();

    /// <summary>Which container this node lives inside (null = top-level)</summary>
    [ObservableProperty] private GraphNode? _parentContainer;

    /// <summary>Which zone within the parent container</summary>
    public ContainerZone? ParentZone { get; set; }

    [ObservableProperty] private double _containerWidth = 500;
    [ObservableProperty] private double _containerHeight = 300;

    // ── Layout constants ───────────────────────────────────────
    public const double HeaderHeight = 34;
    public const double PortSpacing = 26;
    public const double ContainerHeaderHeight = 44;
    public const double ZoneHeaderHeight = 24;
    public const double ZonePadding = 10;

    // ── Constructor ────────────────────────────────────────────
    public GraphNode()
    {
        Inputs.Add(new NodePort
        {
            Name = "In",
            Direction = PortDirection.Input,
            Type = PortType.Pipeline,
            Owner = this
        });
        Outputs.Add(new NodePort
        {
            Name = "Out",
            Direction = PortDirection.Output,
            Type = PortType.Pipeline,
            Owner = this
        });
    }

    // ── Computed layout properties ─────────────────────────────
    public double Height => IsContainer
        ? ContainerHeight
        : Math.Max(
            HeaderHeight + PortSpacing + Math.Max(Inputs.Count, Outputs.Count) * PortSpacing + PortSpacing,
            HeaderHeight + PortSpacing * 3);

    public double EffectiveWidth => IsContainer ? ContainerWidth : Width;

    /// <summary>
    /// Recalculate zone rectangles based on current container size.
    /// Single-zone containers fill the body; dual-zone containers split side by side.
    /// </summary>
    public void RecalcZoneLayout()
    {
        if (!IsContainer || Zones.Count == 0) return;

        double bodyTop = ContainerHeaderHeight;
        double bodyH = ContainerHeight - ContainerHeaderHeight - ZonePadding;
        double pad = ZonePadding;

        if (Zones.Count == 1)
        {
            var z = Zones[0];
            z.OffsetX = pad;
            z.OffsetY = bodyTop;
            z.Width = ContainerWidth - pad * 2;
            z.Height = bodyH;
        }
        else
        {
            double halfW = (ContainerWidth - pad * 3) / 2;
            for (int i = 0; i < Math.Min(Zones.Count, 2); i++)
            {
                Zones[i].OffsetX = pad + i * (halfW + pad);
                Zones[i].OffsetY = bodyTop;
                Zones[i].Width = halfW;
                Zones[i].Height = bodyH;
            }
        }
    }
}
