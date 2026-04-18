using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoSHBlox.Models;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Drives the Import dialog: populates a list of modules discovered via
/// <see cref="PowerShellModuleDiscovery"/> across all detected hosts, filters
/// on user search, and auto-introspects whichever module the user selects so
/// the cmdlet checklist is always one click (plus scan time) away from Save.
/// </summary>
public partial class ImportModuleViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private AvailableModule? _selectedModule;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModuleEmptyHint))]
    private bool _isLoadingModules;
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCmdletEmptyHint))]
    private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Full, unfiltered module list as returned by DiscoverAsync.</summary>
    public ObservableCollection<AvailableModule> AvailableModules { get; } = new();

    /// <summary>Currently visible rows — reduced from <see cref="AvailableModules"/> by <see cref="SearchText"/>.</summary>
    public ObservableCollection<AvailableModule> FilteredModules { get; } = new();

    public ObservableCollection<SelectableCmdlet> DiscoveredCmdlets { get; } = new();

    /// <summary>Shown when the module list is settled but empty — replaces the blank box with a hint.</summary>
    public bool ShowModuleEmptyHint => !IsLoadingModules && FilteredModules.Count == 0;

    /// <summary>Shown when no module has been scanned yet (or the scan returned nothing).</summary>
    public bool ShowCmdletEmptyHint => !IsScanning && DiscoveredCmdlets.Count == 0;

    /// <summary>Host ids from the last successful scan — stamped into the saved catalog.</summary>
    private List<string> _lastScanHostIds = [];

    public ImportModuleViewModel()
    {
        // Empty-state hints depend on collection counts too, not just the
        // Is*ing flags — mirror CollectionChanged onto PropertyChanged for
        // the derived bools so the overlay hides/shows when rows land.
        FilteredModules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowModuleEmptyHint));
        DiscoveredCmdlets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowCmdletEmptyHint));

        // Kick discovery immediately so the list is warming while the user
        // focuses the dialog. LoadModules handles its own errors.
        _ = LoadModulesAsync();
    }

    [RelayCommand]
    private Task RefreshModules() => LoadModulesAsync();

    private async Task LoadModulesAsync()
    {
        if (IsLoadingModules) return;
        IsLoadingModules = true;
        StatusMessage = "Discovering modules on all hosts…";
        AvailableModules.Clear();
        FilteredModules.Clear();
        DiscoveredCmdlets.Clear();

        try
        {
            var result = await PowerShellModuleDiscovery.DiscoverAsync();
            foreach (var m in result.Modules)
                AvailableModules.Add(m);
            RebuildFilter();

            if (result.Modules.Count == 0)
            {
                StatusMessage = result.HostErrors.Count > 0
                    ? $"No modules found. {string.Join("; ", result.HostErrors)}"
                    : "No modules found.";
            }
            else if (result.HostErrors.Count > 0)
            {
                StatusMessage = $"Loaded {result.Modules.Count} module(s). Warnings: {string.Join("; ", result.HostErrors)}";
            }
            else
            {
                StatusMessage = $"Loaded {result.Modules.Count} module(s). Pick one to scan.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Module discovery failed: {ex.Message}";
            Debug.WriteLine($"[ImportModule] Discovery failed: {ex.Message}");
        }
        finally
        {
            IsLoadingModules = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RebuildFilter();

    private void RebuildFilter()
    {
        FilteredModules.Clear();
        var needle = SearchText?.Trim() ?? "";
        IEnumerable<AvailableModule> source = AvailableModules;
        if (needle.Length > 0)
        {
            source = AvailableModules.Where(m =>
                m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (m.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        foreach (var m in source)
            FilteredModules.Add(m);
    }

    partial void OnSelectedModuleChanged(AvailableModule? value)
    {
        if (value == null) return;
        _ = ScanSelectedAsync(value);
    }

    private async Task ScanSelectedAsync(AvailableModule module)
    {
        DiscoveredCmdlets.Clear();
        IsScanning = true;

        var hosts = PowerShellHostRegistry.All;
        if (hosts.Count == 0)
        {
            StatusMessage = "No PowerShell host detected on PATH.";
            IsScanning = false;
            return;
        }

        var hostLabels = string.Join(" + ", hosts.Select(h => h.DisplayName));
        StatusMessage = $"Scanning {module.Name} against {hostLabels}…";

        try
        {
            var perHostResults = await PowerShellIntrospector.IntrospectModuleAllHostsAsync(module.Name);
            if (perHostResults.Count == 0)
            {
                StatusMessage = "All hosts failed to scan the module — check stderr for details.";
                return;
            }

            _lastScanHostIds = perHostResults.Select(r => r.HostId).ToList();

            var editionByHostId = hosts.ToDictionary(h => h.Id, h => h.Edition, StringComparer.OrdinalIgnoreCase);
            var perHostKeyed = perHostResults
                .Select(r => (Edition: editionByHostId.TryGetValue(r.HostId, out var e) ? e : "", Result: r))
                .Where(t => !string.IsNullOrEmpty(t.Edition))
                .ToList();
            var merged = IntrospectionMerger.Merge(perHostKeyed);

            foreach (var c in merged)
            {
                DiscoveredCmdlets.Add(new SelectableCmdlet
                {
                    IsSelected = true,
                    Name = c.Name,
                    Description = c.Description,
                    Parameters = c.Parameters,
                    HasExecIn = c.HasExecIn,
                    HasExecOut = c.HasExecOut,
                    PrimaryPipelineParameter = c.PrimaryPipelineParameter,
                    DataOutputs = c.DataOutputs,
                    KnownParameterSets = c.KnownParameterSets,
                    DefaultParameterSet = c.DefaultParameterSet,
                    SupportedEditions = c.SupportedEditions,
                });
            }

            var resolvedName = perHostResults
                .Select(r => r.ResolvedModuleName)
                .FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? module.Name;
            CategoryName = resolvedName;

            var matchNote = resolvedName != module.Name ? $" (resolved to {resolvedName})" : "";
            StatusMessage = $"Found {merged.Count} cmdlet(s) across {perHostResults.Count} host(s){matchNote}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Debug.WriteLine($"[ImportModule] Scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SaveSelected()
    {
        var selected = DiscoveredCmdlets.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one cmdlet.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            StatusMessage = "Enter a category name.";
            return;
        }

        var catalog = new TemplateCatalogDto
        {
            Version = 2,
            Category = CategoryName.Trim(),
            IntrospectedHosts = new List<string>(_lastScanHostIds),
        };

        foreach (var cmdlet in selected)
        {
            var template = new NodeTemplate
            {
                Name = cmdlet.Name,
                CmdletName = cmdlet.Name,
                Description = cmdlet.Description,
                HasExecIn = cmdlet.HasExecIn,
                HasExecOut = cmdlet.HasExecOut,
                PrimaryPipelineParameter = cmdlet.PrimaryPipelineParameter,
                DataOutputs = cmdlet.DataOutputs.Count > 0
                    ? cmdlet.DataOutputs
                    : [new DataOutputDef { Name = "Out", Type = ParamType.Any, IsPrimary = true }],
                KnownParameterSets = cmdlet.KnownParameterSets,
                DefaultParameterSet = cmdlet.DefaultParameterSet,
                SupportedEditions = new List<string>(cmdlet.SupportedEditions),
            };

            foreach (var p in cmdlet.Parameters)
            {
                var paramType = Enum.TryParse<ParamType>(p.Type, out var pt) ? pt : ParamType.String;
                template.Parameters.Add(new ParameterDef
                {
                    Name = p.Name,
                    Type = paramType,
                    IsMandatory = p.IsMandatory,
                    DefaultValue = p.DefaultValue,
                    Description = p.Description,
                    ValidValues = p.ValidValues ?? [],
                    IsPipelineInput = p.IsPipelineInput,
                    ParameterSets = p.ParameterSets,
                    MandatoryInSets = p.MandatoryInSets,
                    SupportedEditions = new List<string>(p.SupportedEditions),
                });
            }

            catalog.Templates.Add(template);
        }

        try
        {
            await TemplateLoader.SaveCustomCatalogAsync(catalog, CategoryName.Trim());
            StatusMessage = $"Saved {selected.Count} cmdlet(s) to Templates/Custom/{CategoryName.Trim()}.json";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            Debug.WriteLine($"[ImportModule] Save failed: {ex.Message}");
        }
    }
}

public partial class SelectableCmdlet : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<DiscoveredParameter> Parameters { get; set; } = [];

    public bool HasExecIn { get; set; } = true;
    public bool HasExecOut { get; set; } = true;
    public string? PrimaryPipelineParameter { get; set; }
    public List<DataOutputDef> DataOutputs { get; set; } = [];

    public List<string> KnownParameterSets { get; set; } = [];
    public string? DefaultParameterSet { get; set; }

    /// <summary>Editions this cmdlet was discovered in (merged across hosts).</summary>
    public List<string> SupportedEditions { get; set; } = [];
}
