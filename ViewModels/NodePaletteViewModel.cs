using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Palette sidebar ViewModel. Loads templates from TemplateLibrary
/// and provides search/filter functionality.
/// </summary>
public partial class NodePaletteViewModel : ObservableObject
{
    public ObservableCollection<TemplateCategory> Categories { get; } = new();
    public ObservableCollection<TemplateCategory> FilteredCategories { get; } = new();

    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public NodePaletteViewModel()
    {
        var allTemplates = TemplateLibrary.GetAll();

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

        ApplyFilter();
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
public class TemplateCategory
{
    public string Name { get; set; } = "";
    public ObservableCollection<NodeTemplate> Templates { get; set; } = new();
}
