using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
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
}
