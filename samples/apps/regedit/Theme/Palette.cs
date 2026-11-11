using Microsoft.Win32;

namespace ReactorRegedit;

/// <summary>
/// Central palette for the re-skinned Registry Editor.
/// Inspired by the Microsoft.UI.Reactor artwork: deep navy surfaces, a
/// blue → purple → amber accent ramp, and a distinct hue per value type so
/// the different sections of the editor read apart at a glance.
/// </summary>
internal static class Palette
{
    // ── Surfaces ─────────────────────────────────────────────────────
    public const string Background = "#080D19";
    public const string BackgroundAlt = "#0B1220";
    public const string Panel = "#0C1424";
    public const string Surface = "#101A2E";
    public const string SurfaceAlt = "#0E1728";
    public const string Row = "#0C1424";
    public const string RowAlt = "#0F1930";
    public const string RowHover = "#172541";
    public const string RowSelected = "#1B2A49";

    // ── Strokes ──────────────────────────────────────────────────────
    public const string Border = "#223252";
    public const string BorderSoft = "#182440";

    // ── Text ─────────────────────────────────────────────────────────
    public const string TextPrimary = "#E8EEF9";
    public const string TextSecondary = "#9DAFCB";
    public const string TextMuted = "#6B7F9E";

    // ── Accents ──────────────────────────────────────────────────────
    public const string Blue = "#4C8DFF";
    public const string Purple = "#B565F5";
    public const string Pink = "#E879F9";
    public const string Amber = "#F5A623";
    public const string Mint = "#7EE7C7";
    public const string Green = "#6EE7A8";
    public const string Teal = "#4FD1C5";
    public const string Red = "#FF6B81";

    /// <summary>Monospace family used for registry paths and value data.</summary>
    public const string MonoFont = "Cascadia Mono";

    /// <summary>Icon family used across the shell and context menus.</summary>
    public const string IconFont = "Segoe Fluent Icons";

    /// <summary>Accent hue associated with a registry value kind.</summary>
    public static string ForKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => Mint,
        RegistryValueKind.ExpandString => Teal,
        RegistryValueKind.MultiString => Green,
        RegistryValueKind.DWord => Amber,
        RegistryValueKind.QWord => Pink,
        RegistryValueKind.Binary => Blue,
        RegistryValueKind.None => TextMuted,
        _ => TextSecondary,
    };

    /// <summary>Segoe Fluent Icons glyph associated with a registry value kind.</summary>
    public static string IconForKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => "\uE9F5",
        RegistryValueKind.DWord => "\uE8EF",
        RegistryValueKind.QWord => "\uE8EF",
        RegistryValueKind.MultiString => "\uE8FD",
        _ => "\uE8A5",
    };

    /// <summary>Adds an alpha channel to a #RRGGBB color (e.g. for tinted chips).</summary>
    public static string Tint(string hex, byte alpha) =>
        $"#{alpha:X2}{hex.TrimStart('#')}";
}
