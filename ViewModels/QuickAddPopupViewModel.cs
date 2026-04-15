using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Models;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Cursor-local "quick add" popup — Blueprints-style. Opens at the pointer on
/// Tab (empty canvas) or when a wire drag is released on empty space. Shares
/// the catalog + taxonomy + recents with <see cref="NodePaletteViewModel"/>;
/// adds a compatibility filter keyed off the pending wire's source pin plus
/// auto-wire on commit.
///
/// Display mode: always category-grouped, rows default collapsed, typing in
/// the search auto-expands any category with matches (parity with Unreal
/// Blueprints). Flat ranked list would hide the context of where commands
/// live; the collapsible tree preserves it.
/// </summary>
public partial class QuickAddPopupViewModel : ObservableObject
{
    private readonly NodePaletteViewModel _palette;

    /// <summary>True = popup visible.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>Cursor position within the canvas control (popup placement + spawn origin).</summary>
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    /// <summary>Bind the popup's Margin to position it at the cursor (HorizontalAlignment=Left, VerticalAlignment=Top).</summary>
    public Thickness CursorMargin => new(X, Y, 0, 0);

    partial void OnXChanged(double value) => OnPropertyChanged(nameof(CursorMargin));
    partial void OnYChanged(double value) => OnPropertyChanged(nameof(CursorMargin));

    /// <summary>Pending wire drag source (null = Tab / right-click invocation, no wire to finish).</summary>
    [ObservableProperty] private NodePort? _sourcePort;

    /// <summary>Tracks whether a source pin is active — drives the compatibility chip's visibility.</summary>
    public bool HasSourcePort => SourcePort != null;

    /// <summary>Default true whenever <see cref="SourcePort"/> is set; strict primary-compatible filter.</summary>
    [ObservableProperty] private bool _compatibleOnly = true;

    /// <summary>Chip label like "Matches Processes (Collection)" — tells the user what the filter is keyed to.</summary>
    public string CompatibilityChipLabel
    {
        get
        {
            if (SourcePort == null) return "Compatible";
            var name = string.IsNullOrEmpty(SourcePort.Name) ? "pin" : SourcePort.Name;
            if (SourcePort.Kind == PortKind.Exec) return $"\u25B8 exec ({name})";
            return $"\u25CF {name} ({SourcePort.DataType})";
        }
    }

    [ObservableProperty] private string _searchText = "";

    /// <summary>Tag chips mirror the palette's taxonomy — same behavior and styling.</summary>
    public ObservableCollection<TagChip> TagChips { get; } = new();
    public ObservableCollection<string> ActiveTags { get; } = new();

    /// <summary>What the view renders: ordered list of categories with matching templates.</summary>
    public ObservableCollection<TemplateCategory> FilteredCategories { get; } = new();

    public QuickAddPopupViewModel(NodePaletteViewModel palette)
    {
        _palette = palette;
        BuildTagChips();
        ActiveTags.CollectionChanged += (_, _) => ApplyFilter();
    }

    // ── Lifecycle ──────────────────────────────────────────────

    /// <summary>
    /// Show the popup anchored at <paramref name="canvasPos"/>. When
    /// <paramref name="source"/> is non-null, the compatibility chip appears
    /// checked and the filter restricts results to nodes whose primary
    /// pipeline target (or exec pin) accepts the source.
    /// </summary>
    public void OpenAt(double x, double y, NodePort? source)
    {
        X = x;
        Y = y;
        SourcePort = source;
        CompatibleOnly = source != null;
        SearchText = "";
        foreach (var chip in TagChips) chip.IsActive = false;
        ApplyFilter();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        SourcePort = null;
    }

    // ── Property change plumbing ───────────────────────────────

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCompatibleOnlyChanged(bool value) => ApplyFilter();

    partial void OnSourcePortChanged(NodePort? value)
    {
        OnPropertyChanged(nameof(HasSourcePort));
        OnPropertyChanged(nameof(CompatibilityChipLabel));
    }

    // ── Tag chip construction (mirrors palette) ────────────────

    private void BuildTagChips()
    {
        foreach (var tag in PaletteTaxonomy.AllTags)
        {
            var chip = new TagChip { Name = tag, Label = tag };
            chip.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TagChip.IsActive))
                    SyncActiveTagsFromChips();
            };
            TagChips.Add(chip);
        }
    }

    private bool _syncingTags;
    private void SyncActiveTagsFromChips()
    {
        if (_syncingTags) return;
        _syncingTags = true;
        ActiveTags.Clear();
        foreach (var chip in TagChips)
            if (chip.IsActive) ActiveTags.Add(chip.Name);
        _syncingTags = false;
    }

    // ── Filter pipeline ────────────────────────────────────────

    /// <summary>
    /// Collect all templates, apply compat → tags → search → rank, group by
    /// category, auto-expand any category with matches when the search is
    /// non-empty. Empty searches keep rows collapsed so users see structure.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredCategories.Clear();

        var query = SearchText?.Trim() ?? "";
        var tagFilter = new HashSet<string>(ActiveTags);
        bool searching = !string.IsNullOrEmpty(query);
        bool tagFiltering = tagFilter.Count > 0;

        var allTemplates = _palette.Categories.SelectMany(c => c.Templates);

        // Filter stage.
        var kept = new List<(NodeTemplate Template, int Score)>();
        foreach (var t in allTemplates)
        {
            if (CompatibleOnly && SourcePort != null
                && !IsTemplatePrimaryCompatible(t, SourcePort))
                continue;

            if (tagFiltering && !t.Tags.Any(tag => tagFilter.Contains(tag)))
                continue;

            int score = searching ? PaletteSearch.Score(query, t) : 1;
            if (score == 0) continue;

            kept.Add((t, score));
        }

        // Group by category, preserving the palette's natural category order.
        var orderIndex = _palette.Categories
            .Select((c, i) => new { c.Name, i })
            .ToDictionary(x => x.Name, x => x.i);

        var groups = kept
            .GroupBy(k => k.Template.Category)
            .OrderBy(g => orderIndex.TryGetValue(g.Key, out var i) ? i : int.MaxValue)
            .ToList();

        foreach (var g in groups)
        {
            var ordered = searching
                ? g.OrderByDescending(k => k.Score).ThenBy(k => k.Template.Name)
                : g.OrderBy(k => k.Template.Name);

            var cat = new TemplateCategory
            {
                Name = g.Key,
                Templates = new ObservableCollection<NodeTemplate>(ordered.Select(x => x.Template)),
                IsExpanded = searching,  // auto-expand on search, collapsed otherwise
            };
            FilteredCategories.Add(cat);
        }
    }

    // ── Compatibility oracle ───────────────────────────────────

    /// <summary>
    /// Can a node spawned from <paramref name="t"/> accept a wire from
    /// <paramref name="source"/> on its primary compatible pin? Checks
    /// template metadata directly without materializing a GraphNode.
    /// Strict-primary: exec↔exec for exec sources; primary pipeline target
    /// for data output sources; primary data output for data input sources.
    /// </summary>
    private static bool IsTemplatePrimaryCompatible(NodeTemplate t, NodePort source)
    {
        if (source.Kind == PortKind.Exec)
        {
            // Output exec needs an exec-in on the new node, vice versa.
            return source.Direction == PortDirection.Output ? t.HasExecIn : t.HasExecOut;
        }

        // Data source + Output → find a template pipeline-target param compatible with source.DataType.
        if (source.Direction == PortDirection.Output)
        {
            var primaryTarget =
                t.Parameters.FirstOrDefault(p => p.IsPipelineInput)
                ?? (string.IsNullOrEmpty(t.PrimaryPipelineParameter)
                        ? null
                        : t.Parameters.FirstOrDefault(p =>
                            p.Name.Equals(t.PrimaryPipelineParameter, StringComparison.OrdinalIgnoreCase)));
            if (primaryTarget == null) return false;
            return PortCompatibility.IsDataTypeCompatible(source.DataType, primaryTarget.Type);
        }

        // Data source + Input → find template's primary data output compatible with source.DataType.
        if (source.Direction == PortDirection.Input)
        {
            var primaryOut = t.DataOutputs.FirstOrDefault(o => o.IsPrimary)
                           ?? t.DataOutputs.FirstOrDefault();
            if (primaryOut == null) return false;
            return PortCompatibility.IsDataTypeCompatible(primaryOut.Type, source.DataType);
        }

        return false;
    }
}
