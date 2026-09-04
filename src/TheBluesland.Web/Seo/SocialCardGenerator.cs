using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TheBluesland.Web.Seo;

/// <summary>
/// Generates TheBluesland-owned 1200x630 Open Graph/Twitter social card PNGs (US-011 AC3/FR-031): a
/// plain branded background with the site name plus the playlist's own editorial title and summary
/// text baked in server-side, so <c>og:image</c> never points at Spotify's <c>cover_image_url</c>
/// even though it is present in the cache.
///
/// <b>Library choice</b>: SixLabors.ImageSharp + ImageSharp.Drawing over SkiaSharp. Both are
/// reasonable for server-side text-on-image composition in an ASP.NET Core Linux container (spec
/// 12.2). SkiaSharp is MIT-licensed but ships a native <c>libSkiaSharp</c> binary per platform that,
/// on Linux, additionally needs <c>libfontconfig</c>/<c>libfreetype</c> present in the container -
/// an extra apt-get step in whatever Dockerfile US-014 eventually writes. ImageSharp is 100%
/// managed code with zero native dependencies, so this feature needs no image-specific Dockerfile
/// change at all. The trade-off is licensing: ImageSharp/ImageSharp.Drawing use the Six Labors
/// Split License, which is free in perpetuity for individuals and for organisations under
/// US$1,000,000 annual gross revenue that are not backed by private equity/venture capital -
/// TheBluesland (a single-owner hobby/portfolio project) is squarely inside that free tier today.
/// This trade-off should be revisited if the project's ownership or revenue model ever changes.
///
/// <b>Fonts</b>: ImageSharp.Drawing does not embed a font - it needs a real font file to draw
/// glyphs from. Rather than bundling a third-party font asset in this repository (which would need
/// its own license review), this resolves a font from the host's installed system fonts via
/// SixLabors.Fonts' <see cref="SystemFonts"/> lookup, trying a short list of common cross-platform
/// family names first. This works unmodified on this dev machine and on typical CI runners.
/// <b>Remaining risk, deliberately left open here</b>: the eventual production Docker image (US-014)
/// must install at least one TrueType font package (e.g. Debian's <c>fonts-dejavu-core</c>) for
/// this to render real glyphs. If literally no font is installed anywhere, this degrades to a
/// background-only branded image instead of throwing - still a real, non-trivial, TheBluesland-owned
/// PNG (never Spotify's cover URL), just without the editorial text overlay.
/// </summary>
public sealed class SocialCardGenerator
{
    private const int Width = 1200;
    private const int Height = 630;

    private static readonly string[] PreferredFontFamilies =
    [
        "Arial", "Helvetica", "Helvetica Neue", "Liberation Sans", "DejaVu Sans", "Verdana", "Segoe UI",
    ];

    private static readonly Color BackgroundColor = Color.ParseHex("12141c");
    private static readonly Color AccentColor = Color.ParseHex("e8b34a");

    public byte[] Generate(string title, string summary)
    {
        using var image = new Image<Rgba32>(Width, Height);
        image.Mutate(context =>
        {
            context.Fill(BackgroundColor);

            var fontFamily = ResolveFontFamily();
            if (fontFamily is not { } family)
            {
                return;
            }

            var brandFont = new Font(family, 26, FontStyle.Bold);
            var titleFont = new Font(family, 54, FontStyle.Bold);
            var summaryFont = new Font(family, 26, FontStyle.Regular);

            context.DrawText("THEBLUESLAND", brandFont, AccentColor, new PointF(60, 60));
            context.DrawText(WrapText(title, 26), titleFont, Color.White, new PointF(60, 220));

            if (!string.IsNullOrWhiteSpace(summary))
            {
                context.DrawText(WrapText(summary, 62), summaryFont, Color.White, new PointF(60, 460));
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static FontFamily? ResolveFontFamily()
    {
        foreach (var familyName in PreferredFontFamilies)
        {
            if (SystemFonts.TryGet(familyName, out var family))
            {
                return family;
            }
        }

        return SystemFonts.Families.Any() ? SystemFonts.Families.First() : null;
    }

    /// <summary>Naive word wrap - good enough for a short title/summary on a fixed-size card.</summary>
    private static string WrapText(string text, int maxCharsPerLine)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxCharsPerLine)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return string.Join('\n', lines);
    }
}
