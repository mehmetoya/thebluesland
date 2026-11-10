using Shouldly;
using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Review;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, Design section 4.</summary>
public sealed class IssueEmailRendererTests
{
    [Fact]
    public void The_approved_sample_issue_renders_with_its_title_stories_sources_and_ai_note()
    {
        var file = IssueMarkdownWriter.Issue(Week46, DraftLanguage.Turkish, SampleIssue("Türkçe", Card), Card);
        var issue = WeeklyIssueValidator.Read(IssueName(DraftLanguage.Turkish), file).ShouldNotBeNull();

        var email = IssueEmailRenderer.Render(issue);

        email.Subject.ShouldBe("TheBluesland Weekly · 46. hafta");
        email.PreviewText.ShouldBe("Bu hafta Television'ın gitaristine veda ettik, listeler yeniden hesaplandı ve Oasis bir kez daha Oasis'liğini yaptı.");
        email.Html.ShouldStartWith("<h1>TheBluesland Weekly · 46. hafta</h1>");
        email.Html.ShouldContain("<h2>Television'ın kurucularından Richard Lloyd hayatını kaybetti</h2>");
        email.Html.ShouldContain("<em>Neden önemli:</em>");
        email.Html.ShouldContain($"<a href=\"{Card.Picks.First().Links[0].Url}\">{Card.Picks.First().Links[0].Name}</a>");
        email.Html.ShouldContain("<em>Bu sayı yapay zekâ yardımıyla hazırlandı ve TheBluesland editörü tarafından düzenlendi.</em>");
        email.Html.ShouldNotContain("aiAssisted");
        email.Html.ShouldNotContain("---");
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

        html.ShouldStartWith("<h1>T &amp; &lt;b&gt;</h1>");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("javascript:");
        html.ShouldNotContain("http://example.test");
        html.ShouldNotContain("<img");
        html.ShouldNotContain("href=\"http://auto.test");
        html.ShouldContain("<a href=\"https://example.test/a\">good</a>");
        html.ShouldContain("A bad link, a plain one");
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
}
