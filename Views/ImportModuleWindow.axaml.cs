using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentAvalonia.UI.Windowing;
using PoSHBlox.Rendering;

namespace PoSHBlox.Views;

public partial class ImportModuleWindow : AppWindow
{
    public ImportModuleWindow()
    {
        InitializeComponent();

        TitleBar.ExtendsContentIntoTitleBar = false;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;

        LoadIcon();

        Opened += (_, _) =>
        {
            PlatformFeatures?.SetWindowBorderColor(GraphTheme.WindowBorder);
        };
    }

    private void LoadIcon()
    {
        try
        {
            var uri = new Uri("avares://PoSHBlox/Assets/poshblox-icon-512.png");

            using var stream1 = AssetLoader.Open(uri);
            Icon = new Bitmap(stream1);

            using var stream2 = AssetLoader.Open(uri);
            ((Avalonia.Controls.Window)this).Icon = new Avalonia.Controls.WindowIcon(stream2);
        }
        catch
        {
        }
    }
}
