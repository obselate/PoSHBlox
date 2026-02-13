using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;

namespace PoSHBlox;

public partial class ScriptPreviewWindow : AppWindow
{
    public ScriptPreviewWindow()
    {
        InitializeComponent();
    }

    public ScriptPreviewWindow(string script) : this()
    {
        ScriptTextBox.Text = script;
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(ScriptTextBox.Text ?? "");
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
