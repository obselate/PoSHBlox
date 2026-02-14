using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoSHBlox.Models;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

public partial class ImportModuleViewModel : ObservableObject
{
    [ObservableProperty] private string _moduleName = "";
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<SelectableCmdlet> DiscoveredCmdlets { get; } = new();

    [RelayCommand]
    private async System.Threading.Tasks.Task ScanModule()
    {
        if (string.IsNullOrWhiteSpace(ModuleName))
        {
            StatusMessage = "Enter a module name first.";
            return;
        }

        DiscoveredCmdlets.Clear();
        IsScanning = true;
        StatusMessage = $"Scanning {ModuleName}...";

        try
        {
            var result = await PowerShellIntrospector.IntrospectModuleAsync(ModuleName.Trim());

            foreach (var c in result.Cmdlets)
            {
                DiscoveredCmdlets.Add(new SelectableCmdlet
                {
                    IsSelected = true,
                    Name = c.Name,
                    Description = c.Description,
                    Parameters = c.Parameters,
                });
            }

            CategoryName = result.ResolvedModuleName;

            var matchNote = result.ResolvedModuleName != ModuleName.Trim()
                ? $" (resolved to {result.ResolvedModuleName})"
                : "";
            StatusMessage = $"Found {result.Cmdlets.Count} cmdlet(s){matchNote}.";
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
    private async System.Threading.Tasks.Task SaveSelected()
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
            Version = 1,
            Category = CategoryName.Trim(),
        };

        foreach (var cmdlet in selected)
        {
            var template = new NodeTemplate
            {
                Name = cmdlet.Name,
                CmdletName = cmdlet.Name,
                Description = cmdlet.Description,
            };

            foreach (var p in cmdlet.Parameters)
            {
                if (System.Enum.TryParse<ParamType>(p.Type, out var paramType))
                {
                    template.Parameters.Add(new ParameterDef
                    {
                        Name = p.Name,
                        Type = paramType,
                        IsMandatory = p.IsMandatory,
                        DefaultValue = p.DefaultValue,
                        Description = p.Description,
                        ValidValues = p.ValidValues ?? [],
                    });
                }
                else
                {
                    template.Parameters.Add(new ParameterDef
                    {
                        Name = p.Name,
                        Type = ParamType.String,
                        IsMandatory = p.IsMandatory,
                        DefaultValue = p.DefaultValue,
                        Description = p.Description,
                    });
                }
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
    public System.Collections.Generic.List<DiscoveredParameter> Parameters { get; set; } = [];
}
