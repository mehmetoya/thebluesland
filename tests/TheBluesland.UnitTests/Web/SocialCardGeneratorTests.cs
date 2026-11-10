using Shouldly;
using SkiaSharp;
using TheBluesland.Web.Seo;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-011 AC3/FR-031 social card, after the 2026-11-10 move from SixLabors.ImageSharp to SkiaSharp.
/// The old implementation resolved a font from the host's system fonts and silently degraded to a
/// background-only image when none was found; the card now embeds Inter, so text must always be
/// drawn regardless of which fonts the machine (or the Docker image) has installed.
/// </summary>
public sealed class SocialCardGeneratorTests
{
    private static readonly SKColor Background = SKColor.Parse("12141c");

    private readonly SocialCardGenerator _generator = new();

    [Fact]
    public void Generate_returns_a_1200x630_png()
    {
        var png = _generator.Generate("Late Night Blues", "Slow-burning electric blues for after midnight.");

        using var bitmap = SKBitmap.Decode(png);
        bitmap.ShouldNotBeNull();
        bitmap.Width.ShouldBe(1200);
        bitmap.Height.ShouldBe(630);
        png.Take(8).ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Fact]
    public void Generate_draws_visible_text_over_the_background()
    {
        var png = _generator.Generate("Late Night Blues", "Slow-burning electric blues for after midnight.");

        using var bitmap = SKBitmap.Decode(png);
        var nonBackgroundPixels = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y) != Background)
                {
                    nonBackgroundPixels++;
                }
            }
        }

        // Brand line + a two-word title + one summary line cover well over 1% of the sampled grid;
        // a background-only card (the old "no font found" fallback) has zero.
        nonBackgroundPixels.ShouldBeGreaterThan(bitmap.Width * bitmap.Height / 4 / 100);
    }

    [Fact]
    public void Generate_embeds_its_own_fonts_instead_of_relying_on_system_fonts()
    {
        var resources = typeof(SocialCardGenerator).Assembly.GetManifestResourceNames();

        resources.ShouldContain("TheBluesland.Web.Seo.Fonts.Inter-Regular.ttf");
        resources.ShouldContain("TheBluesland.Web.Seo.Fonts.Inter-Bold.ttf");
    }

    [Fact]
    public void Generate_cards_with_different_titles_differ()
    {
        var first = _generator.Generate("Late Night Blues", "Same summary.");
        var second = _generator.Generate("Sunday Morning Soul", "Same summary.");

        first.SequenceEqual(second).ShouldBeFalse();
    }

    [Fact]
    public void Generate_keeps_text_inside_the_card_for_a_very_long_title_and_summary()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Extraordinarily", 30));
        var longSummary = string.Join(' ', Enumerable.Repeat("words", 200));

        var png = _generator.Generate(longTitle, longSummary);

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Height.ShouldBe(630);
        // The bottom margin (last 40px) stays background: wrapped lines are capped, not overflowing.
        for (var x = 0; x < bitmap.Width; x += 4)
        {
            bitmap.GetPixel(x, bitmap.Height - 20).ShouldBe(Background);
        }
    }

    [Fact]
    public void Generate_with_empty_summary_still_returns_a_card()
    {
        var png = _generator.Generate("TheBluesland", "");

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Width.ShouldBe(1200);
    }
}
