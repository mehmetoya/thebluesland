using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Review;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Review;

/// <summary>issue-review spec, Design section 6: everything Mehmet needs, on the PR page.</summary>
public sealed class PullRequestBodyFormatterTests
{
    private const string RankReport = "## Weekly pick · 2026-11-01 07:00 → 2026-11-08 07:00 UTC\n\n| # | Story | Sources | Why |\n|---|---|---|---|\n| 1 | Lloyd | Pitchfork | 3 sources = 4.50 |\n";

    [Fact]
    public void The_body_says_how_to_approve_what_to_check_and_why_each_story_was_picked()
    {
        var warning = new DraftWarning(Card.Picks.First().Key, "body: number “1977” is not in the fact card.");

        var body = PullRequestBodyFormatter.ToMarkdown(
            Week46,
            Card,
            [Draft(DraftLanguage.Turkish, warnings: [warning]), Draft(DraftLanguage.English) with { Model = "lite", FellBackFrom = "flash" }],
            RankReport);

        body.ShouldStartWith("## TheBluesland Weekly · 2026-W46\n");
        body.ShouldContain("Gönderim **2026-11-09** pazartesi");
        body.ShouldContain("**Ready for review**");
        body.ShouldContain("#### TR · ⚠️ 1 uyarı · `test-model`");
        body.ShouldContain("number “1977” is not in the fact card.");
        body.ShouldContain("#### EN · ✅ uyarı yok · `lite` (`flash` yanıt vermedi)");
        body.ShouldContain("### Seçim\n\n| # | Story | Sources | Why |");
        body.ShouldNotContain("## Weekly pick");
    }

    [Fact]
    public void Every_pick_lists_the_sources_its_paragraph_rests_on()
    {
        var body = PullRequestBodyFormatter.ToMarkdown(Week46, Card, [Draft(DraftLanguage.Turkish), Draft(DraftLanguage.English)], RankReport);

        foreach (var story in Card.Picks)
        {
            body.ShouldContain($"{story.Position}. **Headline {story.Key}** — [{story.Links[0].Name}](");
        }
    }

    [Fact]
    public void Each_backup_comes_ready_to_paste_in_both_languages()
    {
        var body = PullRequestBodyFormatter.ToMarkdown(Week46, Card, [Draft(DraftLanguage.Turkish), Draft(DraftLanguage.English)], RankReport);

        var backups = body[body.IndexOf("### Yedekler", StringComparison.Ordinal)..];
        backups.Split("<details>").Length.ShouldBe(4);
        foreach (var story in Card.Backups)
        {
            backups.ShouldContain($"~~~markdown\n## Headline {story.Key}\n");
            backups.ShouldContain($"~~~yaml\n  - key: {story.Key}\n");
        }

        backups.ShouldContain("**TR bölüm**");
        backups.ShouldContain("**EN front matter**");
    }

    [Fact]
    public void Pasting_a_backup_over_a_pick_as_instructed_leaves_a_valid_issue()
    {
        var answer = Answer();
        var draft = Draft(DraftLanguage.Turkish, answer);
        var pick = Card.Picks.First();
        var backup = Card.Backups.First();

        var swapped = draft.IssueMarkdown
            .Replace(IssueMarkdownWriter.FrontMatterEntry(pick, answer.For(pick.Key).Headline), IssueMarkdownWriter.FrontMatterEntry(backup, answer.For(backup.Key).Headline), StringComparison.Ordinal)
            .Replace(
                IssueMarkdownWriter.Section(DraftLanguage.Turkish, answer.For(pick.Key), pick, answer.For(pick.Key).Headline),
                IssueMarkdownWriter.Section(DraftLanguage.Turkish, answer.For(backup.Key), backup, answer.For(backup.Key).Headline),
                StringComparison.Ordinal);

        swapped.ShouldNotBe(draft.IssueMarkdown);
        swapped.ShouldContain($"- key: {backup.Key}\n");
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), swapped).ShouldBeEmpty();
    }

    [Fact]
    public void Model_text_cannot_close_a_fence_or_inject_html()
    {
        var answer = Answer();
        var hostile = answer with { Stories = answer.Stories.Select(story => story with { Headline = "<img src=x> ~~~~ end", Body = "~~~\n# not a heading" }).ToList() };

        var body = PullRequestBodyFormatter.ToMarkdown(Week46, Card, [Draft(DraftLanguage.Turkish, hostile)], RankReport);

        body.ShouldContain("**&lt;img src=x&gt; ~~~~ end**");
        body.ShouldContain("~~~~~markdown\n");
    }

    [Fact]
    public void A_body_too_long_for_github_points_to_the_artifact_instead_of_cutting_backups()
    {
        var answer = Answer();
        var huge = answer with { Stories = answer.Stories.Select(story => story with { Body = new string('a', 20_000) }).ToList() };

        var body = PullRequestBodyFormatter.ToMarkdown(Week46, Card, [Draft(DraftLanguage.Turkish, huge), Draft(DraftLanguage.English, huge)], RankReport);

        body.Length.ShouldBeLessThanOrEqualTo(PullRequestBodyFormatter.MaxLength);
        body.ShouldContain("`weekly-draft` artifact");
        body.ShouldNotContain("~~~markdown");
    }
}
