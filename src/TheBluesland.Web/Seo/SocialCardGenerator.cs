using SkiaSharp;

namespace TheBluesland.Web.Seo;

/// <summary>
/// Generates TheBluesland-owned 1200x630 Open Graph/Twitter social card PNGs (US-011 AC3/FR-031): a
/// plain branded background with the site name plus the playlist's own editorial title and summary
/// text baked in server-side, so <c>og:image</c> never points at Spotify's <c>cover_image_url</c>
/// even though it is present in the cache.
///
/// <b>Library choice (revised 2026-11-10)</b>: SkiaSharp (MIT). This originally used
/// SixLabors.ImageSharp, but every 3.1.x release carries published high-severity advisories and the
/// patched 3.2.0 enforces a Six Labors license key at Release build time - a key-management and
/// licensing dependency this $0, single-owner project does not want. SkiaSharp is free with no key.
/// Its native <c>libSkiaSharp</c> comes from <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c>,
/// which needs no fontconfig/freetype in the container, so the Dockerfile stays unchanged.
///
/// <b>Fonts</b>: Inter (SIL OFL 1.1, <c>Seo/Fonts/Inter-OFL.txt</c>) is embedded in this assembly.
/// The previous version looked up system fonts and fell back to a text-less card when none existed;
/// embedding removes that failure mode entirely - every host renders the same card.
/// </summary>
public sealed class SocialCardGenerator
{
    private const int Width = 1200;
    private const int Height = 630;
    private const float Margin = 60;
    private const float TextWidth = Width - (2 * Margin);

    private const string BrandText = "THEBLUESLAND";
    private const float BrandSize = 26;
    private const float BrandBaseline = 86;

    private const float TitleSize = 54;
    private const float TitleTop = 220;
    private const int TitleMaxLines = 3;

    private const float SummarySize = 26;
    private const float SummaryTop = 460;
    private const int SummaryMaxLines = 3;

    private const float LineSpacing = 1.25f;
    private const string Ellipsis = "…";

    private static readonly SKColor BackgroundColor = SKColor.Parse("004aad");
    private static readonly SKColor AccentColor = SKColor.Parse("ff904b");
    private static readonly SKColor TextColor = SKColors.White;

    private static readonly SKTypeface RegularTypeface = LoadEmbeddedTypeface("Inter-Regular.ttf");
    private static readonly SKTypeface BoldTypeface = LoadEmbeddedTypeface("Inter-Bold.ttf");

    public byte[] Generate(string title, string summary)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(BackgroundColor);

        using var brandFont = CreateFont(BoldTypeface, BrandSize);
        using var titleFont = CreateFont(BoldTypeface, TitleSize);
        using var summaryFont = CreateFont(RegularTypeface, SummarySize);
        using var accentPaint = new SKPaint { Color = AccentColor, IsAntialias = true };
        using var textPaint = new SKPaint { Color = TextColor, IsAntialias = true };

        canvas.DrawText(BrandText, Margin, BrandBaseline, SKTextAlign.Left, brandFont, accentPaint);
        DrawWrapped(canvas, title, titleFont, textPaint, TitleTop, TitleMaxLines);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            DrawWrapped(canvas, summary, summaryFont, textPaint, SummaryTop, SummaryMaxLines);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKFont CreateFont(SKTypeface typeface, float size) =>
        new(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };

    /// <summary>
    /// Word-wraps by measured width (not character count) and caps the line count, ending the last
    /// line with an ellipsis, so arbitrarily long editorial text can never run off the card.
    /// <paramref name="top"/> is the top of the first line; Skia draws text at its baseline.
    /// </summary>
    private static void DrawWrapped(SKCanvas canvas, string text, SKFont font, SKPaint paint, float top, int maxLines)
    {
        var lines = WrapLines(text, font, maxLines);
        var lineHeight = font.Size * LineSpacing;
        var baseline = top + font.Size;

        foreach (var line in lines)
        {
            canvas.DrawText(line, Margin, baseline, SKTextAlign.Left, font, paint);
            baseline += lineHeight;
        }
    }

    private static List<string> WrapLines(string text, SKFont font, int maxLines)
    {
        // Any whitespace (newlines from multi-line YAML summaries, tabs, NBSP) is a word break.
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && font.MeasureText(candidate) > TextWidth)
            {
                lines.Add(FitWithEllipsis(current, font, appendEllipsis: false));
                current = word;
                if (lines.Count == maxLines)
                {
                    // An oversized word may already have been clipped with an ellipsis above.
                    if (!lines[^1].EndsWith(Ellipsis, StringComparison.Ordinal))
                    {
                        lines[^1] = FitWithEllipsis(lines[^1], font);
                    }
                    return lines;
                }
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(FitWithEllipsis(current, font, appendEllipsis: false));
        }

        return lines;
    }

    /// <summary>
    /// Trims a line until it (plus an ellipsis, when <paramref name="appendEllipsis"/>) fits the text
    /// width - also covers a single word too wide for the card on its own.
    /// </summary>
    private static string FitWithEllipsis(string line, SKFont font, bool appendEllipsis = true)
    {
        var suffix = appendEllipsis ? Ellipsis : string.Empty;
        if (font.MeasureText(line + suffix) <= TextWidth)
        {
            return line + suffix;
        }

        var trimmed = line;
        while (trimmed.Length > 1 && font.MeasureText(trimmed + Ellipsis) > TextWidth)
        {
            trimmed = trimmed[..^1];
        }

        // Never leave half of a surrogate pair (e.g. an emoji) dangling before the ellipsis.
        if (trimmed.Length > 1 && char.IsHighSurrogate(trimmed[^1]))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.TrimEnd() + Ellipsis;
    }

    private static SKTypeface LoadEmbeddedTypeface(string fileName)
    {
        var resourceName = $"TheBluesland.Web.Seo.Fonts.{fileName}";
        using var stream = typeof(SocialCardGenerator).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' is missing.");
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' is not a valid font.");
    }
}
