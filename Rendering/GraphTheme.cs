using System.Collections.Generic;
using Avalonia.Media;

namespace PoSHBlox.Rendering;

/// <summary>
/// All visual constants for the node graph — Lonely Planet theme.
/// Swap this out or extend it to change the entire look without touching rendering logic.
/// </summary>
public static class GraphTheme
{
    // ── Canvas ─────────────────────────────────────────────────
    public static readonly Color Background       = Color.FromRgb(6, 13, 22);        // #060D16
    public static readonly Color GridMinor         = Color.FromArgb(128, 28, 52, 80); // #1C3450 ~50% alpha
    public static readonly Color GridMajor         = Color.FromRgb(28, 52, 80);       // #1C3450
    public const double GridSize = 30;
    public const int GridMajorEvery = 5;
    public const double GridDotRadius = 1.0;
    public const double GridDotMajorRadius = 1.8;

    // ── Nodes ──────────────────────────────────────────────────
    public static readonly Color NodeBackground    = Color.FromRgb(15, 34, 54);       // #0F2236
    public static readonly Color NodeBorder        = Color.FromRgb(28, 52, 80);       // #1C3450
    public static readonly Color NodeSelectedBorder = Color.FromRgb(91, 168, 154);    // #5BA89A teal
    public static readonly Color NodeShadow        = Color.FromArgb(102, 0, 0, 0);    // ~40% black
    public const double NodeCornerRadius = 8;
    public const double NodeShadowOffset = 4;

    // ── Containers ─────────────────────────────────────────────
    public static readonly Color ContainerBg       = Color.FromArgb(180, 15, 34, 54);
    public static readonly Color ContainerShadow   = Color.FromArgb(50, 0, 0, 0);
    public static readonly Color ZoneBg            = Color.FromArgb(60, 13, 30, 48);
    public static readonly Color ZoneBorder        = Color.FromArgb(80, 28, 52, 80);
    public static readonly Color ZoneLabel         = Color.FromArgb(120, 168, 186, 201);
    public static readonly Color ZoneHint          = Color.FromArgb(50, 112, 141, 163);
    public static readonly Color ResizeGrip        = Color.FromArgb(100, 112, 141, 163);
    public const double ContainerCornerRadius = 10;
    public const double ContainerShadowOffset = 5;
    public const double ResizeGripSize = 14;

    // ── Wires ──────────────────────────────────────────────────
    public static readonly Color Wire              = Color.FromRgb(127, 191, 239);    // #7FBFEF blue-sky
    public static readonly Color WirePending       = Color.FromRgb(212, 148, 58);     // #D4943A amber
    public const double WireThickness = 2.5;

    // ── Ports ──────────────────────────────────────────────────
    public static readonly Color PortInput         = Color.FromRgb(127, 191, 239);    // #7FBFEF blue-sky
    public static readonly Color PortOutput        = Color.FromRgb(91, 168, 154);     // #5BA89A teal
    public static readonly Color PortCenter        = Color.FromRgb(6, 13, 22);        // canvas bg
    public static readonly Color PortLabel         = Color.FromRgb(168, 186, 201);    // #A8BAC9
    public const double PortRadius = 7;
    public const double PortDotRadius = 3.5;
    public const double PortHitPadding = 4;

    // ── Text ───────────────────────────────────────────────────
    public static readonly Color TextPrimary       = Color.FromRgb(224, 234, 242);    // #E0EAF2 cream
    public static readonly Color TextSecondary     = Color.FromRgb(168, 186, 201);    // #A8BAC9 slate
    public static readonly Color HudText           = Color.FromRgb(112, 141, 163);    // #708DA3 slate-mid
    public static readonly Color HudBg             = Color.FromArgb(180, 15, 34, 54); // secondary bg
    public const string FontFamily = "avares://PoSHBlox/Assets/Fonts#JetBrains Mono";
    public const string FontFamilyMono = "avares://PoSHBlox/Assets/Fonts#JetBrains Mono";

    // ── Header gradient overlay ────────────────────────────────
    public static readonly Color HeaderGradientTop    = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color HeaderGradientBottom  = Color.FromArgb(30, 0, 0, 0);

    // ── Category colors ────────────────────────────────────────
    public static readonly Dictionary<string, Color> CategoryColors = new()
    {
        ["File / Folder"]     = Color.FromRgb(44, 90, 138),   // #2C5A8A blue-steel
        ["Process / Service"] = Color.FromRgb(76, 158, 116),  // #4C9E74 green-moss
        ["Registry"]          = Color.FromRgb(160, 100, 180), // keep
        ["Network / Remote"]  = Color.FromRgb(75, 143, 212),  // #4B8FD4 blue-bright
        ["String / Data"]     = Color.FromRgb(112, 141, 163), // #708DA3 slate-mid
        ["Custom"]            = Color.FromRgb(90, 90, 110),   // keep
        ["Control Flow"]      = Color.FromRgb(112, 96, 168),  // #7060A8 purple
        ["Function"]          = Color.FromRgb(75, 143, 212),  // #4B8FD4 blue-bright
        ["Output"]            = Color.FromRgb(212, 148, 58),  // #D4943A amber
    };

    public static Color GetCategoryColor(string category)
        => CategoryColors.TryGetValue(category, out var c) ? c : CategoryColors["Custom"];
}
