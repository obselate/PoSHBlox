using System.Collections.Generic;
using Avalonia.Media;

namespace PoSHBlox.Rendering;

/// <summary>
/// All visual constants for the node graph.
/// Swap this out or extend it to change the entire look without touching rendering logic.
/// </summary>
public static class GraphTheme
{
    // ── Canvas ─────────────────────────────────────────────────
    public static readonly Color Background       = Color.FromRgb(30, 30, 36);
    public static readonly Color GridMinor         = Color.FromRgb(42, 42, 50);
    public static readonly Color GridMajor         = Color.FromRgb(50, 50, 60);
    public const double GridSize = 30;
    public const int GridMajorEvery = 5;

    // ── Nodes ──────────────────────────────────────────────────
    public static readonly Color NodeBackground    = Color.FromRgb(40, 40, 50);
    public static readonly Color NodeBorder        = Color.FromRgb(60, 60, 72);
    public static readonly Color NodeSelectedBorder = Color.FromRgb(80, 160, 255);
    public static readonly Color NodeShadow        = Color.FromArgb(60, 0, 0, 0);
    public const double NodeCornerRadius = 8;
    public const double NodeShadowOffset = 4;

    // ── Containers ─────────────────────────────────────────────
    public static readonly Color ContainerBg       = Color.FromArgb(180, 30, 30, 38);
    public static readonly Color ContainerShadow   = Color.FromArgb(50, 0, 0, 0);
    public static readonly Color ZoneBg            = Color.FromArgb(60, 20, 20, 28);
    public static readonly Color ZoneBorder        = Color.FromArgb(80, 100, 100, 120);
    public static readonly Color ZoneLabel         = Color.FromArgb(120, 180, 180, 200);
    public static readonly Color ZoneHint          = Color.FromArgb(50, 150, 150, 170);
    public static readonly Color ResizeGrip        = Color.FromArgb(100, 150, 150, 170);
    public const double ContainerCornerRadius = 10;
    public const double ContainerShadowOffset = 5;
    public const double ResizeGripSize = 14;

    // ── Wires ──────────────────────────────────────────────────
    public static readonly Color Wire              = Color.FromRgb(100, 200, 255);
    public static readonly Color WirePending       = Color.FromRgb(255, 180, 60);
    public const double WireThickness = 2.5;

    // ── Ports ──────────────────────────────────────────────────
    public static readonly Color PortInput         = Color.FromRgb(80, 200, 120);
    public static readonly Color PortOutput        = Color.FromRgb(255, 140, 60);
    public static readonly Color PortCenter        = Color.FromRgb(30, 30, 38);
    public static readonly Color PortLabel         = Color.FromRgb(170, 175, 190);
    public const double PortRadius = 7;
    public const double PortDotRadius = 3.5;
    public const double PortHitPadding = 4;

    // ── Text ───────────────────────────────────────────────────
    public static readonly Color TextPrimary       = Color.FromRgb(230, 230, 240);
    public static readonly Color TextSecondary     = Color.FromArgb(160, 255, 255, 255);
    public static readonly Color HudText           = Color.FromRgb(120, 120, 140);
    public static readonly Color HudBg             = Color.FromArgb(180, 20, 20, 26);
    public const string FontFamily = "Inter";

    // ── Header gradient overlay ────────────────────────────────
    public static readonly Color HeaderGradientTop    = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color HeaderGradientBottom  = Color.FromArgb(30, 0, 0, 0);

    // ── Category colors ────────────────────────────────────────
    public static readonly Dictionary<string, Color> CategoryColors = new()
    {
        ["File / Folder"]     = Color.FromRgb(55, 120, 180),
        ["Process / Service"] = Color.FromRgb(180, 80, 60),
        ["Registry"]          = Color.FromRgb(160, 100, 180),
        ["Network / Remote"]  = Color.FromRgb(60, 160, 140),
        ["String / Data"]     = Color.FromRgb(180, 160, 50),
        ["Custom"]            = Color.FromRgb(90, 90, 110),
        ["Control Flow"]      = Color.FromRgb(200, 90, 160),
        ["Function"]          = Color.FromRgb(70, 140, 210),
        ["Output"]            = Color.FromRgb(220, 180, 60),
    };

    public static Color GetCategoryColor(string category)
        => CategoryColors.TryGetValue(category, out var c) ? c : CategoryColors["Custom"];
}
