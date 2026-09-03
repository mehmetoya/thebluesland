using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-010 AC1 / spec 9.3 / SEC-003: curator note Markdown renders to HTML, and raw HTML present in
/// the source Markdown is stripped/escaped rather than passed through (Markdig's DisableHtml()).
/// </summary>
public sealed class CuratorNoteRendererTests
{
    [Fact]
    public void ToSanitizedHtml_renders_ordinary_markdown_formatting()
    {
        var html = CuratorNoteRenderer.ToSanitizedHtml("This playlist is **essential** listening.");

        html.ShouldContain("<strong>essential</strong>");
    }

    [Fact]
    public void ToSanitizedHtml_strips_raw_html_in_the_source_markdown_instead_of_passing_it_through()
    {
        var html = CuratorNoteRenderer.ToSanitizedHtml("Safe text. <script>alert('xss')</script> More text.");

        html.ShouldNotContain("<script>");
        html.ShouldContain("Safe text.");
        html.ShouldContain("More text.");
    }

    /// <summary>
    /// Found in review: DisableHtml() only stops raw HTML from passing through - it does nothing
    /// about a dangerous URL scheme used in ordinary Markdown link syntax, which Markdig still
    /// renders as a normal href. A curator note is edited by Mehmet via PR, but the rendered HTML
    /// is trusted (MarkupString) by every visitor's browser, so this must not slip through.
    /// </summary>
    [Fact]
    public void ToSanitizedHtml_neutralizes_a_javascript_scheme_link()
    {
        var html = CuratorNoteRenderer.ToSanitizedHtml("[Listen now](javascript:alert(document.cookie))");

        html.ShouldNotContain("javascript:");
    }

    [Fact]
    public void ToSanitizedHtml_neutralizes_a_data_scheme_image()
    {
        var html = CuratorNoteRenderer.ToSanitizedHtml("![cover](data:text/html,<script>alert(1)</script>)");

        html.ShouldNotContain("data:text/html");
    }

    [Fact]
    public void ToSanitizedHtml_leaves_an_ordinary_https_link_untouched()
    {
        var html = CuratorNoteRenderer.ToSanitizedHtml("[Open in Spotify](https://open.spotify.com/playlist/abc)");

        html.ShouldContain("href=\"https://open.spotify.com/playlist/abc\"");
    }
}
