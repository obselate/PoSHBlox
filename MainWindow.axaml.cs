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
using PoSHBlox.Models;
using PoSHBlox.Rendering;
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
            PlatformFeatures?.SetWindowBorderColor(GraphTheme.WindowBorder);
            StripCommandBarTransitions();
        };

        // Start with the preview panel collapsed
        MainGrid.RowDefinitions[2].Height = new GridLength(0);

        // Subscribe to VM changes — DataContext may already be set from XAML
        if (DataContext is GraphCanvasViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.QuickAdd.PropertyChanged += OnQuickAddPropertyChanged;
        }
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GraphCanvasViewModel v)
            {
                v.PropertyChanged += OnViewModelPropertyChanged;
                v.QuickAdd.PropertyChanged += OnQuickAddPropertyChanged;
            }
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

                // Del / Backspace — delete selected node (only when not typing)
                case Key.Delete when !inTextBox:
                case Key.Back when !inTextBox:
                    vm.DeleteSelected();
                    e.Handled = true;
                    break;

                // Ctrl+D — duplicate selected node
                case Key.D when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                    vm.DuplicateSelected();
                    e.Handled = true;
                    break;

                // Ctrl+C — copy selection to system clipboard
                case Key.C when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                    _ = CopySelectionAsync(vm);
                    e.Handled = true;
                    break;

                // Ctrl+X — cut selection (copy then delete)
                case Key.X when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                    _ = CutSelectionAsync(vm);
                    e.Handled = true;
                    break;

                // Ctrl+V — paste at the cursor (canvas coords)
                case Key.V when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                    _ = PasteAtCursorAsync(vm);
                    e.Handled = true;
                    break;

                // Ctrl+Z — undo
                case Key.Z when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                    vm.PerformUndo();
                    e.Handled = true;
                    break;

                // Ctrl+Y or Ctrl+Shift+Z — redo
                case Key.Y when !inTextBox && e.KeyModifiers == KeyModifiers.Control:
                case Key.Z when !inTextBox && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                    vm.PerformRedo();
                    e.Handled = true;
                    break;

                // C — collapse / expand selected node
                case Key.C when !inTextBox && e.KeyModifiers == KeyModifiers.None:
                    vm.ToggleCollapseSelected();
                    e.Handled = true;
                    break;

                // F — zoom-to-fit (selection if one is selected, whole graph otherwise)
                case Key.F when !inTextBox && e.KeyModifiers == KeyModifiers.None:
                    GraphCanvas.ZoomToFit(selectionOnly: vm.SelectedNodes.Count > 0);
                    e.Handled = true;
                    break;

                // Ctrl+0 — reset pan/zoom
                case Key.D0 when e.KeyModifiers == KeyModifiers.Control:
                case Key.NumPad0 when e.KeyModifiers == KeyModifiers.Control:
                    vm.ResetView();
                    e.Handled = true;
                    break;

                // Esc — cancel in-flight drag, then deselect, then close cheat sheet / preview
                case Key.Escape:
                    GraphCanvas.CancelDrag();
                    if (vm.IsCheatSheetOpen) vm.IsCheatSheetOpen = false;
                    else if (vm.SelectedNodes.Count > 0) vm.ClearSelection();
                    e.Handled = true;
                    break;

                // ? — toggle cheat sheet. Must require Shift — OemQuestion and Oem2
                // are the same physical key on Windows (VK_OEM_2), so an unshifted
                // "/" was also matching the first case and stealing the palette
                // search shortcut. Guarding both cases on Shift keeps "/" free.
                case Key.OemQuestion when !inTextBox && e.KeyModifiers == KeyModifiers.Shift:
                case Key.Oem2 when !inTextBox && e.KeyModifiers == KeyModifiers.Shift:
                    vm.IsCheatSheetOpen = !vm.IsCheatSheetOpen;
                    e.Handled = true;
                    break;

                // Tab — open the quick-add popup at the cursor (empty-canvas invocation).
                // Only fires when focus isn't in a TextBox so it doesn't steal Tab navigation.
                case Key.Tab when !inTextBox && e.KeyModifiers == KeyModifiers.None:
                    OpenQuickAddAtPointer(vm);
                    e.Handled = true;
                    break;

                // / — focus palette search (only when not typing, unshifted)
                case Key.Oem2 when !inTextBox && e.KeyModifiers == KeyModifiers.None:
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

    private async void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var script = GenerateScript(vm);
        if (string.IsNullOrWhiteSpace(script)) return;

        var host = PowerShellHostRegistry.Active;
        if (host == null)
        {
            Debug.WriteLine("Failed to run script: no PowerShell host detected on PATH.");
            return;
        }

        if (!await ConfirmHostCompatibilityAsync(vm, host)) return;

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"PoSHBlox_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tempPath, script);

            var psi = new ProcessStartInfo
            {
                FileName = host.Executable,
                Arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -File \"{tempPath}\"",
                UseShellExecute = false,
            };

            // Strip PowerShell 7+ module paths from PSModulePath when running under
            // Windows PowerShell 5.1 — 5.1 otherwise loads PS 7 modules whose type
            // data conflicts with its own. Only needed under "powershell" edition;
            // pwsh consumes its own side of PSModulePath cleanly.
            if (string.Equals(host.Edition, "powershell", StringComparison.OrdinalIgnoreCase)
                && psi.Environment.TryGetValue("PSModulePath", out var modulePath)
                && modulePath is not null)
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

    /// <summary>
    /// Checks the graph's cmdlets against <see cref="TemplateLoader.CmdletEditions"/>.
    /// If any cmdlet was only introspected against an edition that isn't the active
    /// host's, prompts the user to continue or cancel.
    /// </summary>
    private async Task<bool> ConfirmHostCompatibilityAsync(GraphCanvasViewModel vm, PowerShellHost host)
    {
        var editionMap = TemplateLoader.CmdletEditions;
        if (editionMap.Count == 0) return true;

        var mismatched = vm.Nodes
            .Select(n => n.CmdletName)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(c => editionMap.TryGetValue(c, out var eds) && !eds.Contains(host.Edition))
            .ToList();

        if (mismatched.Count == 0) return true;

        var preview = mismatched.Take(6).ToList();
        var more = mismatched.Count - preview.Count;
        var list = string.Join(", ", preview) + (more > 0 ? $" (+{more} more)" : "");

        var dialog = new ContentDialog
        {
            Title = "PowerShell host mismatch",
            Content = $"{mismatched.Count} cmdlet(s) in this graph were introspected against a different "
                      + $"PowerShell edition than the active host ({host.DisplayName}):\n\n"
                      + $"{list}\n\n"
                      + "Parameter names or availability may differ. Continue anyway?",
            PrimaryButtonText = "Run anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
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

    // ── Graph clipboard (Ctrl+C/X/V) ───────────────────────────
    //
    // Rides on the OS text clipboard so cut-in-window-A → paste-in-window-B
    // works across PoSHBlox processes. Payload is a tagged JSON envelope
    // (see ClipboardSerializer.MagicString) so paste over arbitrary text
    // silently no-ops instead of throwing.

    private async Task CopySelectionAsync(GraphCanvasViewModel vm)
    {
        var text = vm.CopySelectionToText();
        if (text == null) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await cb.SetTextAsync(text);
    }

    private async Task CutSelectionAsync(GraphCanvasViewModel vm)
    {
        var text = vm.CutSelectionToText();
        if (text == null) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await cb.SetTextAsync(text);
    }

    private async Task PasteAtCursorAsync(GraphCanvasViewModel vm)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
        var text = await cb.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        // Paste at the canvas-space cursor — same UX as quick-add Tab spawn.
        var pos = GraphCanvas.CurrentCanvasPosition;
        vm.PasteFromText(text, pos.X, pos.Y);
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
        => new ScriptGenerator(vm.Nodes, vm.Connections).Generate();

    // ── Quick-add popup handlers ───────────────────────────────

    /// <summary>
    /// Open the quick-add popup at the current pointer position (relative to
    /// the canvas). Routes through the canvas so window-edge clamping applies
    /// equally whether the trigger came from Tab, the context menu, or a wire
    /// drop on empty space.
    /// </summary>
    private void OpenQuickAddAtPointer(GraphCanvasViewModel vm)
    {
        GraphCanvas.OpenQuickAddAtCursor();
    }

    private void OnQuickAddPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Focus the search box once the popup becomes visible — caret inside,
        // first key goes straight to filtering.
        if (e.PropertyName == nameof(QuickAddPopupViewModel.IsOpen)
            && sender is QuickAddPopupViewModel vm && vm.IsOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                QuickAddSearchBox.Focus();
                QuickAddSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void OnDescriptionPressed(object? sender, PointerPressedEventArgs e)
    {
        // Click on a parameter's description line toggles its expanded state.
        // Only left-button clicks; right-click falls through so users can still
        // select text or invoke system context menus in the future.
        if (sender is TextBlock tb
            && tb.DataContext is NodeParameter p
            && e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed)
        {
            p.IsDescriptionExpanded = !p.IsDescriptionExpanded;
            e.Handled = true;
        }
    }

    private void OnQuickAddPanelPressed(object? sender, PointerPressedEventArgs e)
    {
        // Clicks inside the popup must not bubble to the backdrop's close handler.
        e.Handled = true;
    }

    private void OnQuickAddBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        // Click-through on the transparent backdrop cancels the popup.
        // Clicks inside the panel itself don't bubble here (the panel Border
        // has a non-transparent Background, which blocks PointerPressed).
        if (DataContext is GraphCanvasViewModel vm)
        {
            GraphCanvas.CancelDrag(); // drop any pending wire drag too
            vm.QuickAdd.Close();
        }
    }

    private void OnQuickAddCategoryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAddCategory cat })
            cat.IsExpanded = !cat.IsExpanded;
    }

    private void OnQuickAddTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;
        if (sender is not Button { Tag: NodeTemplate template }) return;
        vm.CommitQuickAdd(template);
    }

    private void OnQuickAddSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                GraphCanvas.CancelDrag();
                vm.QuickAdd.Close();
                e.Handled = true;
                break;

            case Key.Down:
                vm.QuickAdd.SelectNext();
                e.Handled = true;
                break;

            case Key.Up:
                vm.QuickAdd.SelectPrevious();
                e.Handled = true;
                break;

            case Key.Enter:
                // Commit the highlighted item (Down/Up moves this). SelectFirstVisible
                // seeds it on popup open, so Enter always has a target when results exist.
                var selected = vm.QuickAdd.SelectedItem?.Template;
                if (selected != null)
                {
                    vm.CommitQuickAdd(selected);
                    e.Handled = true;
                }
                break;
        }
    }
}
