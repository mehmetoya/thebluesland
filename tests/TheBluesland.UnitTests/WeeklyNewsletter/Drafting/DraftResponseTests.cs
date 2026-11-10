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
    public void Parse_rejects_missing_duplicate_or_unknown_keys()
    {
        var keys = Card.Stories.Select(story => story.Key).ToList();

        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer(keys.Skip(1)), Card));
        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer([.. keys, keys[0]]), Card));
        Should.Throw<DraftFormatException>(() => DraftResponse.Parse(FakeAnswer([.. keys.Skip(1), "s-999999"]), Card));
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
