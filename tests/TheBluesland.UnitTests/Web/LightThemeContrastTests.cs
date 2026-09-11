using Shouldly;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/dark-light-mode-toggle.md: WCAG AA contrast regression guard for the light theme
/// palette (<c>src/TheBluesland.Web/Styles/app.css</c>'s <c>:root[data-theme="light"]</c> block).
/// The spec only required verifying these ratios once by hand before shipping (no automated test
/// mandated) - this makes that verification durable, so a future edit to the light palette's hex
/// values can't silently drop below AA (4.5:1 normal text, 3:1 large text/UI) without a red build.
/// Hex values below must be kept in sync with app.css's light-theme block if either changes.
/// </summary>
public sealed class LightThemeContrastTests
{
    private const double MinimumNormalTextRatio = 4.5;

    private const string Bg = "#f7f1e6";
    private const string BgElevated = "#efe6d4";
    private const string BgElevatedHover = "#e6dac2";
    private const string Text = "#2a2216";
    private const string TextMuted = "#6b5f4e";
    private const string Accent = "#8a5220";
    private const string AccentStrong = "#743f19";
    private const string AccentContrast = "#fdf8ef";
    private const string Secondary = "#3f6386";

    public static TheoryData<string, string, string> ForegroundBackgroundPairs => new()
    {
        { "text/bg", Text, Bg },
        { "text-muted/bg", TextMuted, Bg },
        { "accent/bg", Accent, Bg },
        { "accent-strong/bg", AccentStrong, Bg },
        { "accent-contrast/accent", AccentContrast, Accent },
        { "accent-contrast/accent-strong", AccentContrast, AccentStrong },
        { "secondary/bg", Secondary, Bg },
        { "text/bg-elevated", Text, BgElevated },
        { "text-muted/bg-elevated", TextMuted, BgElevated },
        { "text/bg-elevated-hover", Text, BgElevatedHover },
    };

    [Theory]
    [MemberData(nameof(ForegroundBackgroundPairs))]
    public void Light_theme_pair_meets_wcag_aa_for_normal_text(string pairName, string foreground, string background)
    {
        var ratio = ContrastRatio(foreground, background);

        ratio.ShouldBeGreaterThanOrEqualTo(MinimumNormalTextRatio, $"{pairName} was {ratio:F2}:1");
    }

    private static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        var foregroundLuminance = RelativeLuminance(foregroundHex);
        var backgroundLuminance = RelativeLuminance(backgroundHex);
        var (lighter, darker) = foregroundLuminance > backgroundLuminance
            ? (foregroundLuminance, backgroundLuminance)
            : (backgroundLuminance, foregroundLuminance);

        return (lighter + 0.05) / (darker + 0.05);
    }

    // WCAG 2.x relative luminance (sRGB -> linear -> weighted sum).
    private static double RelativeLuminance(string hex)
    {
        hex = hex.TrimStart('#');
        var r = LinearizeChannel(Convert.ToInt32(hex[..2], 16));
        var g = LinearizeChannel(Convert.ToInt32(hex[2..4], 16));
        var b = LinearizeChannel(Convert.ToInt32(hex[4..6], 16));

        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double LinearizeChannel(int channel)
    {
        var normalized = channel / 255.0;
        return normalized <= 0.04045
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }
}
