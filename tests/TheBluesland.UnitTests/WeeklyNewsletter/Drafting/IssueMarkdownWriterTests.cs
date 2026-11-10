using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using YamlDotNet.Serialization;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 4: the file format downstream modules read.</summary>
public sealed class IssueMarkdownWriterTests
{
    private static readonly FactCard Card = W46Card();

    private static DraftResponse Draft() =>
        DraftResponse.Parse(FakeAnswer(Card.Stories.Select(story => story.Key)), Card) is var draft
            ? draft with { Stories = draft.Stories.Select(story => story with { Headline = $"Say \"{story.Key}\" \\ now" }).ToList() }
            : throw new InvalidOperationException();

    [Fact]
    public void Issue_front_matter_round_trips_with_picks_and_their_links()
    {
        var markdown = IssueMarkdownWriter.Issue(Week46, DraftLanguage.Turkish, Draft(), Card);

        var yaml = markdown.Split("---\n")[1];
        var front = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(yaml);
        front["issue"].ShouldBe("2026-W46");
        front["language"].ShouldBe("tr");
        front["title"].ShouldBe("TheBluesland Weekly · 46. hafta");
        front["sendDate"].ShouldBe("2026-11-09");
        front["aiAssisted"].ShouldBe("true");
        var stories = (List<object>)front["stories"];
        stories.Count.ShouldBe(5);
        var first = (Dictionary<object, object>)stories[0];
        first["headline"].ShouldBe($"Say \"{Card.Picks.First().Key}\" \\ now");
        ((List<object>)first["sources"]).Count.ShouldBe(3);
    }

    [Fact]
    public void Subject_and_preview_go_into_the_front_matter_only_when_the_model_wrote_them()
    {
        var with = IssueMarkdownWriter.Issue(Week46, DraftLanguage.English, Draft() with { Subject = "Say \"hi\" · TBL Weekly", Preview = "A line." }, Card);
        var front = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(with.Split("---\n")[1]);

        front["subject"].ShouldBe("Say \"hi\" · TBL Weekly");
        front["preview"].ShouldBe("A line.");
        IssueMarkdownWriter.Issue(Week46, DraftLanguage.English, Draft() with { Subject = null, Preview = null }, Card).ShouldNotContain("subject:");
    }

    [Fact]
    public void Issue_body_has_code_owned_headings_labels_and_links_and_no_ai_note()
    {
        var markdown = IssueMarkdownWriter.Issue(Week46, DraftLanguage.English, Draft(), Card);

        markdown.ShouldContain("\nFive stories, five minutes.\n");
        markdown.ShouldContain($"## Say \"{Card.Picks.First().Key}\" \\ now\n\nBody text.\n\n*Why it matters:* It matters.\n\nSources: [Pitchfork](https://pitchfork.com/");
        markdown.ShouldNotContain("AI assistance");
        Card.Backups.ShouldAllBe(backup => !markdown.Contains($"key: {backup.Key}\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Backups_file_lists_the_three_backups_in_order()
    {
        var markdown = IssueMarkdownWriter.Backups(Week46, DraftLanguage.Turkish, Draft(), Card);

        markdown.ShouldStartWith("---\nbackupsFor: 2026-W46\nlanguage: tr\n---\n");
        markdown.ShouldContain("## Yedek 1 · ");
        markdown.ShouldContain("## Yedek 3 · ");
        markdown.ShouldContain("Kaynaklar: [");
        IssueMarkdownWriter.BackupsFileName(Week46, DraftLanguage.Turkish).ShouldBe(Path.Combine("review", "2026-W46.backups.tr.md"));
    }

    [Fact]
    public void An_over_long_subject_or_preview_is_cut_to_the_validate_weekly_limits()
    {
        // Regression 2026-11-10: a 94-character preview would have failed validate-weekly on the PR.
        var preview = IssueMarkdownWriter.Fit("Vince Neil's partner arrested, Sabaton drop a new track and Type O Negative pass on a reunion.", DraftLanguage.MaxPreview);
        var subject = IssueMarkdownWriter.FitSubject("Richard Lloyd dies, Murderdolls return and much much more news" + DraftLanguage.SubjectTag);

        preview.Length.ShouldBeLessThanOrEqualTo(DraftLanguage.MaxPreview);
        preview.ShouldEndWith("…");
        subject.Length.ShouldBeLessThanOrEqualTo(DraftLanguage.MaxSubject);
        subject.ShouldEndWith(DraftLanguage.SubjectTag);
        IssueMarkdownWriter.Fit("Short.", DraftLanguage.MaxPreview).ShouldBe("Short.");
    }

    // Design round 2 B-7: the cut used to land mid-phrase ("... pass on a…").
    [Theory]
    // Prefers the last sentence end that keeps at least half the room.
    [InlineData("Richard Lloyd dies at 71 and Murderdolls return with a new single. Sabaton announce the tour dates.", 90, "Richard Lloyd dies at 71 and Murderdolls return with a new single.")]
    // No sentence end: cuts before the last " and " / comma, never mid-phrase.
    [InlineData("Vince Neil's partner arrested, Sabaton drop a new track and Type O Negative pass on a reunion.", 90, "Vince Neil's partner arrested, Sabaton drop a new track…")]
    [InlineData("Murderdolls return with a new single, Sabaton announce the tour dates for next spring and fall", 60, "Murderdolls return with a new single…")]
    // Turkish conjunction.
    [InlineData("Richard Lloyd hayatını kaybetti, Murderdolls geri dönüyor ve Sabaton yeni bir turne duyurdu", 80, "Richard Lloyd hayatını kaybetti, Murderdolls geri dönüyor…")]
    // Never ends on a function word.
    [InlineData("Sabaton announce a tour with the legends of heavy metal across the whole of Europe in a", 50, "Sabaton announce a tour with the legends…")]
    public void The_cut_ends_on_a_sentence_or_phrase_boundary_never_on_a_function_word(string text, int max, string expected)
    {
        var fitted = IssueMarkdownWriter.Fit(text, max);

        fitted.ShouldBe(expected);
        fitted.Length.ShouldBeLessThanOrEqualTo(max);
    }
}
