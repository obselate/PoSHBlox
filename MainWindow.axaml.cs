using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Windowing;
using PoSHBlox.Services;
using PoSHBlox.ViewModels;

namespace PoSHBlox;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();

        TitleBar.ExtendsContentIntoTitleBar = false;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;

        LoadIcon();

        Opened += (_, _) =>
        {
            PlatformFeatures?.SetWindowBorderColor(Color.Parse("#3c3c48"));
        };
    }

    private void LoadIcon()
    {
        try
        {
            var uri = new Uri("avares://PoSHBlox/Assets/poshblox-icon-512.png");

            // FluentAvalonia AppWindow.Icon (title bar image)
            using var stream1 = AssetLoader.Open(uri);
            Icon = new Bitmap(stream1);

            // Base Window.Icon (taskbar icon)
            using var stream2 = AssetLoader.Open(uri);
            ((Window)this).Icon = new WindowIcon(stream2);
        }
        catch
        {
            // Continue without icon if asset loading fails
        }
    }

    private void OnPaletteToggleClicked(object? sender, RoutedEventArgs e)
    {
        PalettePanel.IsVisible = PaletteToggle.IsChecked == true;
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

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -File \"{tempPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to run script: {ex.Message}");
        }
    }

    private void OnPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var script = GenerateScript(vm);
        var preview = new ScriptPreviewWindow(script) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        preview.Show(this);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphCanvasViewModel vm) return;

        var script = GenerateScript(vm);
        if (string.IsNullOrWhiteSpace(script)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PowerShell Script",
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

    private static string GenerateScript(GraphCanvasViewModel vm)
    {
        var generator = new ScriptGenerator(vm.Nodes, vm.Connections);
        return generator.Generate();
    }
}
