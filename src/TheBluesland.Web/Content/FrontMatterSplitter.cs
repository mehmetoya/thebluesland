using System.Text;

namespace TheBluesland.Web.Content;

/// <summary>
/// Splits a Markdown file's leading <c>---</c>-delimited YAML front matter from its body.
/// Shared by <see cref="PlaylistContentReader"/> (render path) and
/// <see cref="PlaylistContentValidator"/> (US-006 schema/taxonomy validation) so the two decoupled
/// read paths don't drift on what counts as valid front-matter framing.
/// </summary>
internal static class FrontMatterSplitter
{
    private const string Delimiter = "---";

    public static (string? Yaml, string Body) Split(string content)
    {
        using var reader = new StringReader(content);
        if (reader.ReadLine()?.Trim() != Delimiter)
        {
            return (null, string.Empty);
        }

        var yamlBuilder = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == Delimiter)
            {
                return (yamlBuilder.ToString(), reader.ReadToEnd());
            }

            yamlBuilder.AppendLine(line);
        }

        return (null, string.Empty); // no closing delimiter - not valid front matter
    }
}
