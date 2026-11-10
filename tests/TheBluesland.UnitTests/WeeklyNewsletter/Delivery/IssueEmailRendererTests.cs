using System.Text.RegularExpressions;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Review;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, Design section 4.</summary>
public sealed partial class IssueEmailRendererTests
{
    [Fact]
    public void The_approved_sample_issue_renders_with_its_title_stories_and_sources()
    {
        var file = IssueMarkdownWriter.Issue(Week46, DraftLanguage.Turkish, SampleIssue("Türkçe", Card), Card);
        var issue = WeeklyIssueValidator.Read(IssueName(DraftLanguage.Turkish), file).ShouldNotBeNull();

        var email = IssueEmailRenderer.Render(issue);

        email.Subject.ShouldBe("Richard Lloyd öldü, YouTube Billboard'a dönüyor · TBL Weekly");
        email.PreviewText.ShouldBe("Oasis Rock Hall törenine gitmiyor, GTA VI için Miami'de festival var.");
        email.Html.ShouldContain(">46. hafta</h1>");
        email.Html.ShouldContain("<h2 ");
        email.Html.ShouldContain(">Television'ın kurucularından Richard Lloyd hayatını kaybetti</h2>");
        email.Html.ShouldContain("Neden önemli:</em>");
        var firstLink = Card.Picks.First().Links[0];
        Hrefs(email.Html).ShouldContain(firstLink.Url);
        email.Html.ShouldContain($">{firstLink.Name}</a>");
        email.Html.ShouldNotContain("yapay zekâ");
        email.Html.ShouldNotContain("aiAssisted");
        email.Html.ShouldNotContain("---");
    }

    [Theory]
    [InlineData("tr", "Bu haftanın dinlemesi", "Çalma listesini aç")]
    [InlineData("en", "This week's listen", "Open the playlist")]
    public void A_linked_playlist_adds_a_block_before_the_footer_and_none_otherwise(string code, string heading, string linkText)
    {
        var language = DraftLanguage.All.Single(candidate => candidate.Code == code);
        var issue = new WeeklyIssue("2026-W46", language, "Title", new DateOnly(2026, 11, 9), "Intro.\n\n## Story\n\nBody.", Playlist: "anadolu-nights");

        var html = IssueEmailRenderer.Render(issue).Html;

        html.ShouldContain(heading);
        html.ShouldContain($"href=\"https://thebluesland.com/{code}/playlists/anadolu-nights\"");
        html.ShouldContain($">{linkText}</a>");
        html.IndexOf(heading, StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("/weekly\"", html.IndexOf(heading, StringComparison.Ordinal), StringComparison.Ordinal));
        IssueEmailRenderer.Render(issue with { Playlist = null }).Html.ShouldNotContain(heading);
    }

    [Fact]
    public void Subject_and_preview_fall_back_to_the_title_and_the_intro_for_issues_without_them()
    {
        var issue = new WeeklyIssue("2026-W46", DraftLanguage.English, "Old title", new DateOnly(2026, 11, 9), "First sentence. Second.\n\n## Story\n\nBody.");

        var email = IssueEmailRenderer.Render(issue);

        email.Subject.ShouldBe("Old title");
        email.PreviewText.ShouldBe("First sentence.");
        IssueEmailRenderer.Render(issue with { Subject = "Hook · TBL Weekly", Preview = "A line." }).ShouldSatisfyAllConditions(
            rendered => rendered.Subject.ShouldBe("Hook · TBL Weekly"),
            rendered => rendered.PreviewText.ShouldBe("A line."));
    }

    [Fact]
    public void Raw_html_is_escaped_and_only_https_links_survive()
    {
        var issue = new WeeklyIssue("2026-W46", DraftLanguage.English, "T & <b>", new DateOnly(2026, 11, 9), """
            Intro <script>alert(1)</script> here.

            ## Story

            A [bad](javascript:alert(1)) link, a [plain](http://example.test) one, a [good](https://example.test/a) one,
            a pixel ![x](https://tracker.test/p.gif) and <http://auto.test>.
            """);

        var html = IssueEmailRenderer.Render(issue).Html;

        Regex.IsMatch(html, "<h1[^>]*>T &amp; &lt;b&gt;</h1>").ShouldBeTrue();
        html.ShouldContain("&lt;script&gt;");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("javascript:");
        html.ShouldNotContain("http://example.test");
        html.ShouldNotContain("tracker.test");
        html.ShouldNotContain("href=\"http://auto.test");
        Hrefs(html).ShouldAllBe(url => url.StartsWith("https://", StringComparison.Ordinal));
        Hrefs(html).ShouldContain("https://example.test/a");
        Regex.IsMatch(html, "<a href=\"https://example.test/a\"[^>]*>good</a>").ShouldBeTrue();
        Regex.Matches(html, "<img ").Count.ShouldBe(1); // only the brand logo; the body's image is stripped
        html.ShouldContain("A bad link, a plain one");
    }

    [Fact]
    public void The_email_is_a_branded_inline_styled_single_column_with_numbered_stories()
    {
        var file = IssueMarkdownWriter.Issue(Week46, DraftLanguage.Turkish, SampleIssue("Türkçe", Card), Card);
        var issue = WeeklyIssueValidator.Read(IssueName(DraftLanguage.Turkish), file).ShouldNotBeNull();

        var html = IssueEmailRenderer.Render(issue).Html;

        html.ShouldStartWith("<div lang=\"tr\" dir=\"ltr\" style=\"max-width:600px;");
        html.ShouldNotContain("<style");
        html.ShouldNotContain("<table");
        html.ShouldNotContain("<script");
        var image = Regex.Match(html, "<img [^>]*>").Value;
        image.ShouldContain("src=\"https://thebluesland.com/images/logo-blue.png\"");
        image.ShouldContain("alt=\"TheBluesland Weekly\"");
        image.ShouldContain("width=\"160\"");
        Regex.Matches(html, "<img ").Count.ShouldBe(1);
        Hrefs(html).ShouldContain("https://thebluesland.com/tr/weekly/2026-W46");
        html.ShouldContain(">Web'de oku</a>");
        Regex.Matches(html, @">0[1-5]</p>").Count.ShouldBe(5);
        Regex.Matches(html, "border-top:1px solid #e8eff9").Count.ShouldBeGreaterThanOrEqualTo(5);
        // The "Neden önemli" label and its border are one tone.
        html.ShouldContain("border-left:4px solid #a8501c");
        html.ShouldContain("text-transform:uppercase;color:#a8501c;");
        html.ShouldContain("color:#004aad;text-decoration:underline;");
        html.ShouldContain(">Arşiv</a> · <a href=\"https://thebluesland.com/en/weekly/2026-W46\" lang=\"en\"");
        html.ShouldContain(">Read in English</a>");
    }

    [Fact]
    public void The_english_email_links_the_archive_and_the_turkish_edition()
    {
        var issue = new WeeklyIssue("2026-W46", DraftLanguage.English, "TheBluesland Weekly · Week 46", new DateOnly(2026, 11, 9), "Intro.\n\n## Story\n\nBody.");

        var html = IssueEmailRenderer.Render(issue).Html;

        html.ShouldStartWith("<div lang=\"en\" dir=\"ltr\"");
        html.ShouldContain(">Week 46</h1>");
        html.ShouldContain("Issue 46 · 9 November 2026 · ");
        html.ShouldContain(">Read on the web</a>");
        html.ShouldContain(">Archive</a> · <a href=\"https://thebluesland.com/tr/weekly/2026-W46\" lang=\"tr\"");
        html.ShouldContain(">Türkçe oku</a>");
    }

    [Fact]
    public void A_story_shows_at_most_two_sources_the_first_being_primary()
    {
        var issue = new WeeklyIssue("2026-W46", DraftLanguage.English, "T", new DateOnly(2026, 11, 9), """
            Intro.

            ## Story

            Body.

            *Why it matters:* Because.

            Sources: [One](https://one.test/a) · [Two](https://two.test/b) · [Three](https://three.test/c)
            """);

        var html = IssueEmailRenderer.Render(issue).Html;

        Hrefs(html).ShouldContain("https://one.test/a");
        Hrefs(html).ShouldContain("https://two.test/b");
        Hrefs(html).ShouldNotContain("https://three.test/c");
        html.ShouldNotContain("Three");
        html.ShouldContain(">Two</a></p>");
    }

    [Fact]
    public void The_preview_line_is_the_intros_first_sentence_and_bounded()
    {
        var longIntro = new string('a', 400) + ". Second.";
        var issue = new WeeklyIssue("2026-W46", DraftLanguage.English, "T", new DateOnly(2026, 11, 9), $"{longIntro}\n\n## Story\n\nBody.");

        var preview = IssueEmailRenderer.Render(issue).PreviewText;

        preview.Length.ShouldBe(IssueEmailRenderer.MaxPreviewLength);
        preview.ShouldEndWith("…");
    }

    private static List<string> Hrefs(string html) =>
        HrefPattern().Matches(html).Select(match => match.Groups[1].Value).ToList();

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex HrefPattern();
}
