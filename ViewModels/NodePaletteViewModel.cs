using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Models;
using PoSHBlox.Rendering;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Palette sidebar ViewModel. Loads templates from JSON files and presents them
/// grouped by category for browsing, flat-ranked for search, or filtered by
/// tag chips for intent ("only things that don't mutate state", etc.).
///
/// The raw template list lives in <see cref="Categories"/> (natural grouping).
/// <see cref="FilteredCategories"/> is what the view renders: it's either the
/// natural grouping (maybe with a "Recent" pseudo-category on top) or a single
/// synthetic "Results" category holding a flat score-ranked list.
/// </summary>
public partial class NodePaletteViewModel : ObservableObject
{
    public ObservableCollection<TemplateCategory> Categories { get; } = new();
    public ObservableCollection<TemplateCategory> FilteredCategories { get; } = new();

    /// <summary>Currently toggled tag chips. Template must have at least one matching tag when non-empty.</summary>
    public ObservableCollection<string> ActiveTags { get; } = new();

    /// <summary>Toggleable chip view models (one per tag in the taxonomy).</summary>
    public ObservableCollection<TagChip> TagChips { get; } = new();

    /// <summary>Cmdlet names of templates spawned this session, most-recent first.</summary>
    public List<string> RecentCmdletNames { get; } = new();
    private const int RecentLimit = 8;

    [ObservableProperty] private string _searchText = "";

    private ObservableCollection<GraphNode>? _graphNodes;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public NodePaletteViewModel()
    {
        LoadTemplates();
        BuildTagChips();
        ActiveTags.CollectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }

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

    // ── Public interactions ────────────────────────────────────

    public void SetGraphNodes(ObservableCollection<GraphNode> nodes)
    {
        _graphNodes = nodes;
    }

    public void Reload()
    {
        Categories.Clear();
        FilteredCategories.Clear();
        LoadTemplates();
        SyncFunctionTemplates();
    }

    /// <summary>Toggle a tag chip. Idempotent add/remove.</summary>
    public void ToggleTag(string tag)
    {
        if (!ActiveTags.Remove(tag)) ActiveTags.Add(tag);
    }

    /// <summary>Called when a template is spawned — moves it to the head of the recent list.</summary>
    public void NoteSpawn(NodeTemplate template)
    {
        var key = string.IsNullOrEmpty(template.CmdletName) ? template.Name : template.CmdletName;
        if (string.IsNullOrEmpty(key)) return;
        RecentCmdletNames.Remove(key);
        RecentCmdletNames.Insert(0, key);
        if (RecentCmdletNames.Count > RecentLimit)
            RecentCmdletNames.RemoveRange(RecentLimit, RecentCmdletNames.Count - RecentLimit);
        ApplyFilter();
    }

    // ── Function-container sync (unchanged) ─────────────────────

    public void SyncFunctionTemplates()
    {
        var existing = Categories.FirstOrDefault(c => c.Name == "Functions");
        if (existing != null)
            Categories.Remove(existing);

        if (_graphNodes == null)
        {
            ApplyFilter();
            return;
        }

        var functions = _graphNodes
            .Where(n => n.ContainerType == ContainerType.Function)
            .ToList();

        if (functions.Count > 0)
        {
            var templates = new ObservableCollection<NodeTemplate>();

            foreach (var fn in functions)
            {
                var fnName = fn.Parameters
                    .FirstOrDefault(p => p.Name == "FunctionName")?.EffectiveValue
                    ?? "Invoke-MyFunction";

                var args = fn.Parameters.Where(p => p.IsArgument).ToList();

                var template = new NodeTemplate
                {
                    Name = fnName,
                    Category = "Functions",
                    Description = $"Call {fnName}",
                    CmdletName = fnName,
                    // V2 shape: user functions get standard exec pins and a
                    // single primary Any-typed output. NodeFactory builds the
                    // per-parameter data inputs from Parameters automatically.
                    HasExecIn = true,
                    HasExecOut = true,
                    DataOutputs = [new DataOutputDef { Name = "Out", Type = ParamType.Any, IsPrimary = true }],
                    Parameters = args.Select(a => new ParameterDef
                    {
                        Name = a.Name,
                        Type = a.Type,
                        IsMandatory = a.IsMandatory,
                        DefaultValue = a.DefaultValue,
                        Description = a.Description,
                    }).ToList(),
                    Tags = [..PaletteTaxonomy.DeriveTags(fnName)],
                };

                templates.Add(template);
            }

            Categories.Insert(0, new TemplateCategory
            {
                Name = "Functions",
                Templates = templates,
            });
        }

        ApplyFilter();
    }

    // ── Loading ────────────────────────────────────────────────

    private void LoadTemplates()
    {
        var allTemplates = TemplateLoader.LoadAll();

        var grouped = allTemplates
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key)
            .Select(g => new TemplateCategory
            {
                Name = g.Key,
                Templates = new ObservableCollection<NodeTemplate>(g.OrderBy(t => t.Name)),
            });

        foreach (var cat in grouped)
            Categories.Add(cat);
    }

    // ── Filter pipeline ────────────────────────────────────────

    /// <summary>
    /// Three display modes:
    ///   1. Search active or tag filters active → one flat "Results" category,
    ///      score-ranked (search) or alphabetical (tags only).
    ///   2. No filters, with recent spawns → "Recent" pseudo-category on top,
    ///      then the natural categories.
    ///   3. No filters, no recents → natural categories only.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredCategories.Clear();

        var query = SearchText?.Trim() ?? "";
        var tagFilter = new HashSet<string>(ActiveTags);

        bool searching = !string.IsNullOrEmpty(query);
        bool tagFiltering = tagFilter.Count > 0;

        var allTemplates = Categories.SelectMany(c => c.Templates);

        if (searching || tagFiltering)
        {
            // Flat ranked list, possibly tag-gated.
            var scored = new List<(NodeTemplate Template, int Score)>();
            foreach (var t in allTemplates)
            {
                if (tagFiltering && !t.Tags.Any(tag => tagFilter.Contains(tag)))
                    continue;

                int score = searching ? PaletteSearch.Score(query, t) : 1; // non-zero placeholder for tag-only mode
                if (score == 0) continue;
                scored.Add((t, score));
            }

            scored.Sort((a, b) =>
                b.Score != a.Score
                    ? b.Score.CompareTo(a.Score)
                    : string.Compare(a.Template.Name, b.Template.Name, System.StringComparison.OrdinalIgnoreCase));

            if (scored.Count > 0)
            {
                FilteredCategories.Add(new TemplateCategory
                {
                    Name = "Results",
                    Templates = new ObservableCollection<NodeTemplate>(scored.Select(s => s.Template)),
                });
            }
            return;
        }

        // No filters — prepend a Recent pseudo-category when we have spawns this session.
        if (RecentCmdletNames.Count > 0)
        {
            // Some cmdlets (e.g. New-Item) legitimately appear in multiple catalog
            // categories; keep the first template seen for the Recent lookup so
            // the same key doesn't throw on ToDictionary.
            var byKey = allTemplates
                .GroupBy(t => string.IsNullOrEmpty(t.CmdletName) ? t.Name : t.CmdletName)
                .ToDictionary(g => g.Key, g => g.First());
            var recent = RecentCmdletNames
                .Select(k => byKey.TryGetValue(k, out var tpl) ? tpl : null)
                .Where(t => t != null)
                .Cast<NodeTemplate>()
                .ToList();

            if (recent.Count > 0)
            {
                FilteredCategories.Add(new TemplateCategory
                {
                    Name = "Recent",
                    Templates = new ObservableCollection<NodeTemplate>(recent),
                });
            }
        }

        foreach (var cat in Categories)
            FilteredCategories.Add(cat);
    }
}

/// <summary>Grouping container for the palette sidebar.</summary>
public partial class TemplateCategory : ObservableObject
{
    public string Name { get; set; } = "";
    public ObservableCollection<NodeTemplate> Templates { get; set; } = new();
    [ObservableProperty] private bool _isExpanded = true;

    public ISolidColorBrush CategoryBrush =>
        new SolidColorBrush(GraphTheme.GetCategoryColor(Name));
}

/// <summary>Single tag filter chip. XAML binds IsActive two-way to a ToggleButton.</summary>
public partial class TagChip : ObservableObject
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    [ObservableProperty] private bool _isActive;
}
