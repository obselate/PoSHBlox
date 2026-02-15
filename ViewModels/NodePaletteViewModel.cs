using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Models;
using PoSHBlox.Rendering;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Palette sidebar ViewModel. Loads templates from JSON files
/// and provides search/filter functionality.
/// </summary>
public partial class NodePaletteViewModel : ObservableObject
{
    public ObservableCollection<TemplateCategory> Categories { get; } = new();
    public ObservableCollection<TemplateCategory> FilteredCategories { get; } = new();

    [ObservableProperty] private string _searchText = "";

    private ObservableCollection<GraphNode>? _graphNodes;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public NodePaletteViewModel()
    {
        LoadTemplates();
        ApplyFilter();
    }

    /// <summary>
    /// Store a reference to the graph's node collection so function
    /// templates can be rebuilt when the graph changes.
    /// </summary>
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

    /// <summary>
    /// Rebuild the dynamic "Functions" palette category from the current
    /// graph's function containers. Each function container produces a
    /// callable node template (regular cmdlet-style node, not a container).
    /// The category is only present when at least one function exists.
    /// </summary>
    public void SyncFunctionTemplates()
    {
        // Remove existing Functions category
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
                    InputCount = 1,
                    OutputCount = 1,
                    InputNames = ["In"],
                    OutputNames = ["Out"],
                    Parameters = args.Select(a => new ParameterDef
                    {
                        Name = a.Name,
                        Type = a.Type,
                        IsMandatory = a.IsMandatory,
                        DefaultValue = a.DefaultValue,
                        Description = a.Description,
                    }).ToList(),
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

    private void ApplyFilter()
    {
        FilteredCategories.Clear();
        var query = SearchText?.Trim().ToLowerInvariant() ?? "";

        foreach (var cat in Categories)
        {
            if (string.IsNullOrEmpty(query))
            {
                FilteredCategories.Add(cat);
                continue;
            }

            var matched = cat.Templates
                .Where(t => t.Name.ToLowerInvariant().Contains(query)
                         || t.Description.ToLowerInvariant().Contains(query)
                         || t.Category.ToLowerInvariant().Contains(query))
                .ToList();

            if (matched.Count > 0)
            {
                FilteredCategories.Add(new TemplateCategory
                {
                    Name = cat.Name,
                    Templates = new ObservableCollection<NodeTemplate>(matched),
                });
            }
        }
    }
}

/// <summary>
/// Grouping container for the palette sidebar.
/// </summary>
public partial class TemplateCategory : ObservableObject
{
    public string Name { get; set; } = "";
    public ObservableCollection<NodeTemplate> Templates { get; set; } = new();
    [ObservableProperty] private bool _isExpanded = true;

    public ISolidColorBrush CategoryBrush =>
        new SolidColorBrush(GraphTheme.GetCategoryColor(Name));
}
