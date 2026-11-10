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
    public void Issue_body_has_code_owned_headings_labels_links_and_ai_note()
    {
        var markdown = IssueMarkdownWriter.Issue(Week46, DraftLanguage.English, Draft(), Card);

        markdown.ShouldContain("\nFive stories this week.\n");
        markdown.ShouldContain($"## Say \"{Card.Picks.First().Key}\" \\ now\n\nBody text.\n\n*Why it matters:* It matters.\n\nSources: [Pitchfork](https://pitchfork.com/");
        markdown.TrimEnd().ShouldEndWith("*This issue was prepared with AI assistance and edited by TheBluesland's editor.*");
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
}
