using System.Text.RegularExpressions;

namespace Zev.Api.Domain;

// Platform colour palette (one row). The web app turns it into CSS variables (--zev-*); the factory values are the
// colours the stylesheets had before themes existed, so the default theme looks exactly as before.
public sealed partial class SystemThemeSettings
{
    public const int SingletonId = 1;
    public const string CustomThemeName = "Custom";
    public static readonly DateTimeOffset SeededUtc = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    public int Id { get; set; } = SingletonId;
    public string ActiveThemeName { get; set; } = "Default";
    public string PrimaryColor { get; set; } = "#176d50";
    public string SecondaryColor { get; set; } = "#4f6070";
    public string AccentColor { get; set; } = "#e9f5ef";
    public string BackgroundColor { get; set; } = "#f5f7f8";
    public string SurfaceColor { get; set; } = "#ffffff";
    public string TextColor { get; set; } = "#17233a";
    public string HeaderBgColor { get; set; } = "#ffffff";
    public string HeaderTextColor { get; set; } = "#17233a";
    public string MenuBgColor { get; set; } = "#102e31";
    public string MenuTextColor { get; set; } = "#e3eeeb";
    public string MenuActiveColor { get; set; } = "#23524d";
    public string BorderColor { get; set; } = "#e4e9ec";
    public DateTimeOffset UpdatedAt { get; set; } = SeededUtc;
    public Guid? UpdatedByUserId { get; set; }

    // Colour columns in a fixed order (API, validation, presets, migrations).
    public static readonly string[] ColorNames =
    [
        nameof(PrimaryColor), nameof(SecondaryColor), nameof(AccentColor), nameof(BackgroundColor), nameof(SurfaceColor),
        nameof(TextColor), nameof(HeaderBgColor), nameof(HeaderTextColor), nameof(MenuBgColor), nameof(MenuTextColor),
        nameof(MenuActiveColor), nameof(BorderColor)
    ];

    public sealed record Preset(string Name, string Label, string[] Colors);

    // Predefined palettes; colours follow ColorNames. "Default" is the factory palette.
    public static readonly Preset[] Presets =
    [
        new("Default", "Default ERP", Factory().Colors()),
        new("DarkNavy", "Tamna / Tamnoplava",
            ["#2563eb", "#94a3b8", "#1e3a5f", "#0f172a", "#1e293b", "#e2e8f0", "#1e293b", "#e2e8f0", "#020617", "#cbd5e1", "#1d4ed8", "#334155"]),
        new("HighContrast", "Visoki kontrast",
            ["#0033cc", "#000000", "#ffff00", "#ffffff", "#ffffff", "#000000", "#000000", "#ffffff", "#000000", "#ffffff", "#0033cc", "#000000"]),
    ];

    public static readonly string[] ThemeNames = [.. Presets.Select(preset => preset.Name), CustomThemeName];

    public static SystemThemeSettings Factory() => new();

    public string[] Colors() => [.. ColorNames.Select(name => (string)typeof(SystemThemeSettings).GetProperty(name)!.GetValue(this)!)];

    public void SetColors(IReadOnlyList<string> colors)
    {
        for (var index = 0; index < ColorNames.Length; index++)
            typeof(SystemThemeSettings).GetProperty(ColorNames[index])!.SetValue(this, colors[index]);
    }

    // "#RRGGBB" only (stored lower case), so the value is safe to put into a CSS custom property.
    public static string? NormalizeColor(string? value) =>
        value is not null && HexColor().IsMatch(value) ? value.ToLowerInvariant() : null;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
