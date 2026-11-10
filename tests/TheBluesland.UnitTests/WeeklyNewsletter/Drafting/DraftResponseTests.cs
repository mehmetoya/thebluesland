using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 3 (schema row).</summary>
public sealed class DraftResponseTests
{
    private static readonly FactCard Card = W46Card();

    [Fact]
    public void Parse_accepts_an_answer_covering_every_key_once()
    {
        var draft = DraftResponse.Parse(FakeAnswer(Card.Stories.Select(story => story.Key)), Card);

        draft.Stories.Count.ShouldBe(8);
        draft.For(Card.Stories[0].Key).Headline.ShouldBe($"Headline {Card.Stories[0].Key}");
    }

    [Fact]
    public void Parse_reads_subject_and_preview_and_treats_blank_ones_as_absent()
    {
        var keys = Card.Stories.Select(story => story.Key);
        var draft = DraftResponse.Parse(FakeAnswer(keys), Card);
        var blank = DraftResponse.Parse(FakeAnswer(keys).Replace("\"A different preview line.\"", "\" \"", StringComparison.Ordinal), Card);

        (draft.Subject, draft.Preview).ShouldBe(("Two hooks · TBL Weekly", "A different preview line."));
        blank.Preview.ShouldBeNull();
    }

    [Fact]
    public void Parse_rejects_missing_duplicate_or_unknown_keys()
    {
        var keys = Card.Stories.Select(story => story.Key).ToList();

        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer(keys.Skip(1)), Card));
        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer([.. keys, keys[0]]), Card));
        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer([.. keys.Skip(1), "s-999999"]), Card));
    }

    [Fact]
    public void A_skipped_backup_is_filled_from_the_card_and_marked()
    {
        // Regression: 2026-11-10 the fallback model wrote only the five picks and the whole draft failed.
        var picks = Card.Picks.Select(story => story.Key).ToList();
        var skipped = Card.Backups.First();

        var draft = DraftResponse.Parse(FakeAnswer(picks), Card);

        draft.Stories.Count.ShouldBe(8);
        draft.For(skipped.Key).Headline.ShouldBe(skipped.Articles[0].Title);
        draft.For(skipped.Key).WhyItMatters.ShouldBe(DraftResponse.MissingBackupNote);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"intro\":\"a\",\"outro\":\"b\",\"stories\":[{\"key\":\"s-1\",\"headline\":\"\",\"body\":\"x\",\"whyItMatters\":\"y\"}]}")]
    public void Parse_rejects_malformed_answers(string json)
    {
        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(json, Card));
    }
}
