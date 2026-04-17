using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoSHBlox.Services;

namespace PoSHBlox.ViewModels;

/// <summary>
/// Bindable facade over <see cref="PowerShellHostRegistry"/> for the status-bar
/// chip. Singleton — the host is application-global, not graph-scoped.
///
/// Subscribes to <see cref="PowerShellHostRegistry.ActiveHostChanged"/> so picker
/// changes made from anywhere (chip flyout, future settings page) refresh the UI.
/// </summary>
public sealed partial class HostStatusViewModel : ObservableObject
{
    public static HostStatusViewModel Instance { get; } = new();

    /// <summary>True while <see cref="ReintrospectAllCommand"/> is running. Disables the button + shows status.</summary>
    [ObservableProperty] private bool _isReintrospecting;

    /// <summary>Last report from <see cref="ReintrospectAllCommand"/> — surfaced under the button.</summary>
    [ObservableProperty] private string _reintrospectStatus = "";

    /// <summary>Visibility helper for the status TextBlock — true when a message is live.</summary>
    public bool HasReintrospectStatus => !string.IsNullOrEmpty(ReintrospectStatus);

    partial void OnReintrospectStatusChanged(string value) => OnPropertyChanged(nameof(HasReintrospectStatus));

    private HostStatusViewModel()
    {
        PowerShellHostRegistry.ActiveHostChanged += () =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(SelectedHost));
            OnPropertyChanged(nameof(IsHealthy));
            OnPropertyChanged(nameof(HasMultipleHosts));
        };
    }

    /// <summary>All detected hosts — source for the flyout list.</summary>
    public IReadOnlyList<PowerShellHost> AllHosts => PowerShellHostRegistry.All;

    /// <summary>Chip label when a host is available, otherwise a "not detected" warning string.</summary>
    public string DisplayName =>
        PowerShellHostRegistry.Active?.DisplayName ?? "No PowerShell detected";

    /// <summary>Two-way binding target for the flyout's ListBox.SelectedItem.</summary>
    public PowerShellHost? SelectedHost
    {
        get => PowerShellHostRegistry.Active;
        set
        {
            if (value != null)
                PowerShellHostRegistry.Active = value;
        }
    }

    /// <summary>False when no host was detected — chip switches to a warning tone.</summary>
    public bool IsHealthy => PowerShellHostRegistry.Active != null;

    /// <summary>Whether the flyout should surface switch controls (hidden with only one host).</summary>
    public bool HasMultipleHosts => AllHosts.Count > 1;

    /// <summary>
    /// Re-scan every Custom catalog against all detected hosts so per-param
    /// <c>SupportedEditions</c> is repopulated. Runs on a background thread —
    /// each module scan is itself IO-bound (process launch + wait), but running
    /// serially keeps stderr readable and avoids flooding the system with PS
    /// processes.
    /// </summary>
    [RelayCommand]
    private async Task ReintrospectAll()
    {
        if (IsReintrospecting) return;
        IsReintrospecting = true;
        ReintrospectStatus = "Re-introspecting custom catalogs…";

        try
        {
            var report = await Task.Run(CatalogReintrospector.RunAsync);
            ReintrospectStatus = report.Failed == 0
                ? $"Done. {report.Succeeded} rescanned, {report.Skipped} skipped."
                : $"Done. {report.Succeeded} rescanned, {report.Failed} failed, {report.Skipped} skipped — check log.";
        }
        catch (System.Exception ex)
        {
            ReintrospectStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsReintrospecting = false;
        }
    }
}
