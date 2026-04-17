using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Services;

namespace PoSHBlox.Models;

public partial class GraphNode : ObservableObject
{
    public string Id { get; init; } = IdMint.ShortGuid();

    // ── Core identity ──────────────────────────────────────────
    [ObservableProperty] private string _title = "New Node";
    [ObservableProperty] private string _category = "Custom";
    [ObservableProperty] private string _scriptBody = "# Your script here";
    public string CmdletName { get; set; } = "";
    public bool IsCmdletNode => !string.IsNullOrEmpty(CmdletName);

    /// <summary>
    /// Structural role of the node. <see cref="NodeKind.Cmdlet"/> is the
    /// full V2 shape; <see cref="NodeKind.Value"/> is a compact single-output
    /// literal producer (see <c>values.json</c> catalog).
    /// </summary>
    public NodeKind Kind { get; set; } = NodeKind.Cmdlet;
    public bool IsValueNode => Kind == NodeKind.Value;

    /// <summary>True for free-form script-body nodes — not a cmdlet wrapper and not a value literal.</summary>
    public bool IsScriptNode => !IsCmdletNode && !IsValueNode;

    /// <summary>
    /// For <see cref="NodeKind.Value"/> nodes: the PowerShell expression this
    /// node emits. May be a fixed literal (<c>$true</c>, <c>$PSScriptRoot</c>)
    /// or a template containing <c>{0}</c>, which is substituted with
    /// <c>Parameters[0].EffectiveValue</c> at codegen / render time (enables
    /// parametric nodes like <c>$env:{0}</c>).
    /// </summary>
    public string ValueExpression { get; set; } = "";

    /// <summary>
    /// Resolve <see cref="ValueExpression"/> against the node's first parameter
    /// (if any) for rendering and codegen. Fixed value nodes fall through with
    /// no substitution.
    /// </summary>
    public string ResolvedValueExpression
    {
        get
        {
            if (string.IsNullOrEmpty(ValueExpression)) return "";
            if (!ValueExpression.Contains("{0}")) return ValueExpression;
            var arg = Parameters.Count > 0 ? Parameters[0].EffectiveValue : "";
            return ValueExpression.Replace("{0}", arg ?? "");
        }
    }

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

    /// <summary>
    /// When true the node hides non-mandatory, unwired, empty-value data inputs
    /// (e.g. WhatIf, PassThru). Exec pins and data outputs always stay visible.
    /// Persisted in .pblx; toggled from keyboard / context menu.
    /// </summary>
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>
    /// Sets this node's cmdlet declares. Empty = single-set / legacy template;
    /// no set picker shown and no set-based filtering applied. Populated by
    /// <see cref="PoSHBlox.Services.NodeFactory"/> from the template.
    /// </summary>
    public string[] KnownParameterSets { get; set; } = [];

    /// <summary>Currently-active parameter set. Empty when <see cref="KnownParameterSets"/> is empty.</summary>
    [ObservableProperty] private string _activeParameterSet = "";

    /// <summary>True when the cmdlet declares more than one set — drives the picker's visibility.</summary>
    public bool HasMultipleSets => KnownParameterSets.Length > 1;

    // ── Ports & parameters ─────────────────────────────────────
    public ObservableCollection<NodePort> Inputs { get; } = new();
    public ObservableCollection<NodePort> Outputs { get; } = new();
    public ObservableCollection<NodeParameter> Parameters { get; } = new();

    /// <summary>
    /// Validation findings attached to this node. Populated by the VM from
    /// <see cref="PoSHBlox.Services.GraphValidator"/>; the renderer + properties
    /// panel bind to it for the red border, warning badge, and issue list.
    /// </summary>
    public ObservableCollection<GraphIssue> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    // ── Container support ──────────────────────────────────────
    public ContainerType ContainerType { get; set; } = ContainerType.None;
    public bool IsContainer => ContainerType != ContainerType.None;
    public bool IsFunctionContainer => ContainerType == ContainerType.Function;
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

    /// <summary>Fixed body height for <see cref="NodeKind.Value"/> nodes — one row, no header.</summary>
    public const double ValueNodeHeight = 30;

    /// <summary>Height of the dedicated exec-pin row that sits between the header and data rows.</summary>
    public const double ExecRowHeight = 20;

    public const double ContainerHeaderHeight = 44;
    public const double ZoneHeaderHeight = 24;
    public const double ZonePadding = 10;
    public const double MinContainerWidth = 300;
    public const double MinContainerHeight = 200;

    // ── Constructor ────────────────────────────────────────────
    public GraphNode()
    {
        // Keep HasIssues / HasErrors observable — the collection itself mutates
        // without firing PropertyChanged on the derived bools; ColChanged does.
        Issues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(HasErrors));
        };

        // V2 default shape: ExecIn (top-left), ExecOut (top-right),
        // one primary data output of type Any.
        Inputs.Add(new NodePort
        {
            Name = "",
            Kind = PortKind.Exec,
            Direction = PortDirection.Input,
            Owner = this,
        });
        Outputs.Add(new NodePort
        {
            Name = "",
            Kind = PortKind.Exec,
            Direction = PortDirection.Output,
            Owner = this,
        });
        Outputs.Add(new NodePort
        {
            Name = "Out",
            Kind = PortKind.Data,
            Direction = PortDirection.Output,
            DataType = ParamType.Any,
            IsPrimary = true,
            Owner = this,
        });
    }

    // ── V2 port helpers ────────────────────────────────────────
    public IEnumerable<NodePort> DataInputs  => Inputs.Where(p => p.Kind == PortKind.Data);
    public IEnumerable<NodePort> DataOutputs => Outputs.Where(p => p.Kind == PortKind.Data);
    public NodePort? ExecInPort              => Inputs.FirstOrDefault(p => p.Kind == PortKind.Exec);
    public NodePort? ExecOutPort             => Outputs.FirstOrDefault(p => p.Kind == PortKind.Exec);
    public NodePort? PrimaryDataOutput       => DataOutputs.FirstOrDefault(p => p.IsPrimary)
                                              ?? DataOutputs.FirstOrDefault();
    public NodePort? PrimaryPipelineTarget   => DataInputs.FirstOrDefault(p => p.IsPrimaryPipelineTarget);

    /// <summary>
    /// Data input pins that should actually render. Combines both filters —
    /// collapse state AND active-parameter-set membership — so row-indexing
    /// stays consistent with what the renderer ultimately draws. Using only
    /// the collapse filter here produces "empty rows" on nodes where the
    /// active-set filter hides pins in the middle of the list.
    /// </summary>
    public IEnumerable<NodePort> VisibleDataInputs =>
        DataInputs.Where(IsPortVisible);

    /// <summary>How many data input pins are hidden — used by the renderer hint.</summary>
    public int HiddenDataInputCount => DataInputs.Count() - VisibleDataInputs.Count();

    private bool IsDataInputVisibleWhenCollapsed(NodePort port)
    {
        // Unpaired data inputs (e.g. ForEach's Source) always stay visible.
        if (string.IsNullOrEmpty(port.ParameterName)) return true;

        // Primary pipeline target is where upstream data lands — keep it visible
        // even when collapsed so users don't lose the main input affordance on
        // cmdlets like Write-Host where Object is the message field.
        if (port.IsPrimaryPipelineTarget) return true;

        var param = Parameters.FirstOrDefault(p => p.Name == port.ParameterName);
        if (param == null) return true;
        return param.IsMandatory || param.IsWired || !string.IsNullOrWhiteSpace(param.Value);
    }

    /// <summary>
    /// Should this port render / accept input? Exec pins and data outputs are
    /// always visible; data inputs are filtered first by the active parameter
    /// set (pins whose paired param doesn't belong to the active set are
    /// hidden regardless of collapse), then by collapse state.
    /// </summary>
    public bool IsPortVisible(NodePort port)
    {
        if (port.Kind == PortKind.Exec) return true;
        if (port.Direction == PortDirection.Output) return true;

        // Active-parameter-set filter — only applies when the node's cmdlet
        // actually declares multiple sets and the param opts into specific ones.
        if (!string.IsNullOrEmpty(port.ParameterName) && KnownParameterSets.Length > 0)
        {
            var param = Parameters.FirstOrDefault(p => p.Name == port.ParameterName);
            if (param != null && param.ParameterSets.Length > 0
                && !param.ParameterSets.Contains(ActiveParameterSet, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        if (!IsCollapsed) return true;
        return IsDataInputVisibleWhenCollapsed(port);
    }

    partial void OnActiveParameterSetChanged(string value)
    {
        // Active-set changes flip visibility + height the same way collapse does.
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(VisibleDataInputs));
        OnPropertyChanged(nameof(HiddenDataInputCount));
    }

    // ── Computed layout properties ─────────────────────────────
    /// <summary>
    /// Height sized to the parameter-row count. Exec pins sit in the header so
    /// only data pins contribute rows. Minimum keeps script-only nodes visible.
    /// </summary>
    /// <summary>True when the node has at least one exec pin — drives the exec-row reservation.</summary>
    public bool HasExecRow => ExecInPort != null || ExecOutPort != null;

    public double Height => IsContainer
        ? ContainerHeight
        : IsValueNode
            ? ValueNodeHeight
            : Math.Max(
                HeaderHeight
                  + (HasExecRow ? ExecRowHeight : 0)
                  + PortSpacing
                  + Math.Max(VisibleDataInputs.Count(), DataOutputs.Count()) * PortSpacing
                  + PortSpacing
                  + (HiddenDataInputCount > 0 ? PortSpacing : 0),
                HeaderHeight + PortSpacing * 2);

    /// <summary>
    /// Reactive re-layout hook: when IsCollapsed flips, the layout-derived
    /// properties (Height, visible pin lists) change — but CommunityToolkit
    /// only fires a change notice for IsCollapsed itself. Explicitly notify
    /// so the renderer recomputes.
    /// </summary>
    partial void OnIsCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(VisibleDataInputs));
        OnPropertyChanged(nameof(HiddenDataInputCount));
    }

    public double EffectiveWidth => IsContainer ? ContainerWidth : Width;

    /// <summary>
    /// Expand this container if any child extends beyond zone bounds.
    /// Only grows, never shrinks below current size (preserves manual resize).
    /// Snaps to 10px grid and calls RecalcZoneLayout() if changed.
    /// </summary>
    public void AutoGrowToFitChildren()
    {
        if (!IsContainer || Zones.Count == 0) return;

        double pad = ZonePadding;
        double requiredW, requiredH;

        if (Zones.Count == 1)
        {
            var (contentW, contentH) = Zones[0].GetContentBounds(this);
            requiredW = contentW + pad * 2;
            requiredH = contentH + ContainerHeaderHeight + pad;
        }
        else
        {
            double maxZoneW = 0, maxZoneH = 0;
            foreach (var zone in Zones)
            {
                var (cw, ch) = zone.GetContentBounds(this);
                if (cw > maxZoneW) maxZoneW = cw;
                if (ch > maxZoneH) maxZoneH = ch;
            }
            requiredW = 2 * maxZoneW + pad * 3;
            requiredH = maxZoneH + ContainerHeaderHeight + pad;
        }

        double newW = Math.Max(ContainerWidth, requiredW);
        double newH = Math.Max(ContainerHeight, requiredH);

        // Snap to 10px grid
        newW = Math.Ceiling(newW / 10) * 10;
        newH = Math.Ceiling(newH / 10) * 10;

        if (Math.Abs(newW - ContainerWidth) > 0.5 || Math.Abs(newH - ContainerHeight) > 0.5)
        {
            ContainerWidth = newW;
            ContainerHeight = newH;
            RecalcZoneLayout();

            // Propagate growth to parent container
            ParentContainer?.AutoGrowToFitChildren();
        }
    }

    /// <summary>
    /// Shrink this container to tightly fit content, respecting minimums.
    /// Called when a node is removed from a zone. Snaps to 10px grid.
    /// </summary>
    public void ShrinkToFitChildren()
    {
        if (!IsContainer || Zones.Count == 0) return;

        double pad = ZonePadding;
        double requiredW, requiredH;

        if (Zones.Count == 1)
        {
            var (contentW, contentH) = Zones[0].GetContentBounds(this);
            requiredW = contentW + pad * 2;
            requiredH = contentH + ContainerHeaderHeight + pad;
        }
        else
        {
            double maxZoneW = 0, maxZoneH = 0;
            foreach (var zone in Zones)
            {
                var (cw, ch) = zone.GetContentBounds(this);
                if (cw > maxZoneW) maxZoneW = cw;
                if (ch > maxZoneH) maxZoneH = ch;
            }
            requiredW = 2 * maxZoneW + pad * 3;
            requiredH = maxZoneH + ContainerHeaderHeight + pad;
        }

        double newW = Math.Max(MinContainerWidth, requiredW);
        double newH = Math.Max(MinContainerHeight, requiredH);

        // Snap to 10px grid
        newW = Math.Ceiling(newW / 10) * 10;
        newH = Math.Ceiling(newH / 10) * 10;

        if (Math.Abs(newW - ContainerWidth) > 0.5 || Math.Abs(newH - ContainerHeight) > 0.5)
        {
            ContainerWidth = newW;
            ContainerHeight = newH;
            RecalcZoneLayout();
        }

        // Propagate shrink to parent container
        ParentContainer?.ShrinkToFitChildren();
    }

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
