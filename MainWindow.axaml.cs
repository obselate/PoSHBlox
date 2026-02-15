using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using PoSHBlox.Services;
using PoSHBlox.ViewModels;
using PoSHBlox.Views;

using TemplateCategory = PoSHBlox.ViewModels.TemplateCategory;

namespace PoSHBlox;

public partial class MainWindow : AppWindow
{
    private DispatcherTimer? _previewTimer;
    private GridLength _savedPreviewHeight = new(200);
    private bool _isClosingConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        TitleBar.ExtendsContentIntoTitleBar = false;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;

        LoadIcon();

        Opened += (_, _) =>
        {
            PlatformFeatures?.SetWindowBorderColor(Color.Parse("#0B1929"));
            StripCommandBarTransitions();
        };

        // Start with the preview panel collapsed
        MainGrid.RowDefinitions[2].Height = new GridLength(0);

        // Subscribe to VM changes — DataContext may already be set from XAML
        if (DataContext is GraphCanvasViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GraphCanvasViewModel v)
                v.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Don't intercept keys when user is typing in a TextBox
        bool inTextBox = TopLevel.GetTopLevel(this) is { FocusManager: { } fm }
                         && fm.GetFocusedElement() is TextBox;

        if (DataContext is GraphCanvasViewModel vm)
        {
            switch (e.Key)
            {
                // P — toggle palette (only when not typing)
                case Key.P when !inTextBox && e.KeyModifiers == KeyModifiers.None:
                    vm.IsPaletteOpen = !vm.IsPaletteOpen;
                    e.Handled = true;
                    break;

                // F5 — run script
                case Key.F5:
                    OnRunClicked(this, e);
                    e.Handled = true;
                    break;

                // Ctrl+N — new graph
                case Key.N when e.KeyModifiers == KeyModifiers.Control:
                    OnNewClicked(this, e);
                    e.Handled = true;
                    break;

                // Ctrl+O — open project
                case Key.O when e.KeyModifiers == KeyModifiers.Control:
                    OnOpenClicked(this, e);
                    e.Handled = true;
                    break;

                // Ctrl+S — save project (.pblx)
                case Key.S when e.KeyModifiers == KeyModifiers.Control:
                    OnSavePblxClicked(this, e);
                    e.Handled = true;
                    break;

                // Ctrl+E — export script (.ps1)
                case Key.E when e.KeyModifiers == KeyModifiers.Control:
                    OnExportPs1Clicked(this, e);
                    e.Handled = true;
                    break;

                // Alt+S — toggle script panel
                case Key.S when e.KeyModifiers == KeyModifiers.Alt:
                    vm.IsPreviewOpen = !vm.IsPreviewOpen;
                    e.Handled = true;
                    break;

                // Del — delete selected node (only when not typing)
                case Key.Delete when !inTextBox:
                    vm.DeleteSelected();
                    e.Handled = true;
                    break;

                // / — focus palette search (only when not typing)
                case Key.Oem2 when !inTextBox && e.KeyModifiers == KeyModifiers.None:  // '/' key
                    vm.IsPaletteOpen = true;
                    PaletteSearchBox.Focus();
                    PaletteSearchBox.SelectAll();
                    e.Handled = true;
                    break;
            }
        }

        if (!e.Handled)
            base.OnKeyDown(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GraphCanvasViewModel.IsPreviewOpen))
        {
            var vm = (GraphCanvasViewModel)sender!;
            if (vm.IsPreviewOpen)
                OpenPreviewPanel(vm);
            else
                ClosePreviewPanel();
        }
    }

    private void OpenPreviewPanel(GraphCanvasViewModel vm)
    {
        MainGrid.RowDefinitions[2].Height = _savedPreviewHeight;
        PreviewPanel.IsVisible = true;
        PreviewSplitter.IsVisible = true;

        RefreshPreview(vm);

        _previewTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _previewTimer.Tick += OnPreviewTimerTick;
        _previewTimer.Start();
    }

    private void ClosePreviewPanel()
    {
        // Save current height so re-opening restores it
        if (MainGrid.RowDefinitions[2].Height.Value > 0)
            _savedPreviewHeight = MainGrid.RowDefinitions[2].Height;

        if (_previewTimer != null)
        {
            _previewTimer.Tick -= OnPreviewTimerTick;
            _previewTimer.Stop();
        }

        PreviewPanel.IsVisible = false;
        PreviewSplitter.IsVisible = false;
        MainGrid.RowDefinitions[2].Height = new GridLength(0);
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is GraphCanvasViewModel vm)
            RefreshPreview(vm);
    }

    private void RefreshPreview(GraphCanvasViewModel vm)
    {
        PreviewTextBox.Text = GenerateScript(vm);
    }

    private void LoadIcon()
    {
        try
        {
            var uri = new Uri("avares://PoSHBlox/Assets/poshblox-icon-512.png");

            using var stream1 = AssetLoader.Open(uri);
            Icon = new Bitmap(stream1);

            using var stream2 = AssetLoader.Open(uri);
            ((Window)this).Icon = new WindowIcon(stream2);
        }
        catch
        {
            // Continue without icon if asset loading fails
        }
    }

    /// <summary>
    /// Removes BrushTransitions from FluentAvalonia's CommandBar button templates.
    /// The 83 ms transition animates from transparent to our dark per-control resource
    /// overrides, briefly interpolating through a bright intermediate (the "white flash").
    /// XAML Style setters cannot override template-inline Transitions (lower priority),
    /// so we set them at LocalValue priority here after the template is applied.
    /// </summary>
    private void StripCommandBarTransitions()
    {
        foreach (var border in this.GetVisualDescendants().OfType<Border>())
        {
            if (border.Name == "AppBarButtonInnerBorder")
                border.Transitions = new Transitions();
        }
    }

    private void OnCategoryHeaderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TemplateCategory cat)
            cat.IsExpanded = !cat.IsExpanded;
    }

    private void OnTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is NodeTemplate template
            && DataContext is GraphCanvasViewModel vm)
        {
            vm.SpawnFromTemplate(template);
        }
    }

    private void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var script = GenerateScript(vm);
        if (string.IsNullOrWhiteSpace(script)) return;

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"PoSHBlox_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tempPath, script);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -File \"{tempPath}\"",
                UseShellExecute = false,
            };

            // Strip PowerShell 7+ module paths from PSModulePath so PS 5.1
            // doesn't load PS 7 modules whose type data conflicts with PS 5.1.
            if (psi.Environment.TryGetValue("PSModulePath", out var modulePath))
            {
                var filtered = string.Join(";", modulePath.Split(';')
                    .Where(p => !Regex.IsMatch(p, @"\\powershell\\\d", RegexOptions.IgnoreCase)));
                psi.Environment["PSModulePath"] = filtered;
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to run script: {ex.Message}");
        }
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;
        if (!await ConfirmDiscardAsync()) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PoSHBlox Project",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PoSHBlox Project") { Patterns = new[] { "*.pblx" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
            }
        });

        if (files.Count == 0) return;
        var file = files[0];

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            ProjectSerializer.Deserialize(json, vm);
            vm.CurrentFilePath = file.TryGetLocalPath();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open project: {ex.Message}");
        }
    }

    private async void OnSavePblxClicked(object? sender, RoutedEventArgs e)
    {
        await SaveProjectAsync();
    }

    private async Task<bool> SaveProjectAsync()
    {
        if (DataContext is not GraphCanvasViewModel vm) return false;

        string? path = vm.CurrentFilePath;

        if (path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save PoSHBlox Project",
                DefaultExtension = "pblx",
                SuggestedFileName = "MyProject",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PoSHBlox Project") { Patterns = new[] { "*.pblx" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                }
            });

            if (file == null) return false;
            path = file.TryGetLocalPath();
            if (path == null) return false;
        }

        try
        {
            var json = ProjectSerializer.Serialize(vm, vm.ProjectCreatedUtc);
            await File.WriteAllTextAsync(path, json);
            vm.CurrentFilePath = path;
            vm.IsDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save project: {ex.Message}");
            return false;
        }
    }

    private async void OnExportPs1Clicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var script = GenerateScript(vm);
        if (string.IsNullOrWhiteSpace(script)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PowerShell Script",
            DefaultExtension = "ps1",
            SuggestedFileName = "PoSHBlox-Script",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PowerShell Script") { Patterns = new[] { "*.ps1" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
            }
        });

        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(script);
    }

    private async void OnCopyPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(PreviewTextBox.Text ?? "");
    }

    private async void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;
        if (await ConfirmDiscardAsync())
            vm.NewGraph();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (DataContext is not GraphCanvasViewModel vm || !vm.IsDirty)
            return true;

        var dialog = new ContentDialog
        {
            Title = "Unsaved Changes",
            Content = "Your current graph has unsaved changes. What would you like to do?",
            PrimaryButtonText = "Save First",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();

        return result switch
        {
            ContentDialogResult.Primary => await SaveProjectAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isClosingConfirmed && DataContext is GraphCanvasViewModel vm && vm.IsDirty)
        {
            e.Cancel = true;
            if (await ConfirmDiscardAsync())
            {
                _isClosingConfirmed = true;
                Close();
            }
            return;
        }
        base.OnClosing(e);
    }

    private async void OnImportModuleClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var dialog = new ImportModuleWindow();
        await dialog.ShowDialog(this);

        vm.Palette.Reload();
    }

    private static string GenerateScript(GraphCanvasViewModel vm)
    {
        var generator = new ScriptGenerator(vm.Nodes, vm.Connections);
        return generator.Generate();
    }
}
