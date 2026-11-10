using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>ADR-0006 madde 1 / issue-drafting spec, Design sections 1–2: what the model may see.</summary>
public sealed class FactCardAndPromptTests
{
    private static readonly FactCard Card = W46Card();

    [Fact]
    public void Card_holds_the_five_picks_then_the_three_backups()
    {
        Card.Picks.Count().ShouldBe(5);
        Card.Backups.Count().ShouldBe(3);
        Card.Picks.First().Articles[0].Title.ShouldBe("Richard Lloyd, Television Guitarist, Dies at 75");
        Card.Picks.First().Links.Select(link => link.Name).ShouldBe(["Pitchfork", "Stereogum", "NME"]);
        Card.Picks.First().Why.ShouldBe("3 sources · obituary +1.5 · brand +1.25 = 5.75");
    }

    [Fact]
    public void Prompt_contains_the_voice_guide_the_language_and_every_story_key()
    {
        var prompt = DraftPromptBuilder.Build(DraftLanguage.Turkish, VoiceGuide(), Card);

        prompt.ShouldContain(VoiceGuide());
        prompt.ShouldContain("Write the issue in Turkish");
        Card.Stories.ShouldAllBe(story => prompt.Contains($"Story key: {story.Key} "));
        prompt.ShouldContain("- Pitchfork, 2026-11-07: \"Richard Lloyd, Television Guitarist, Dies at 75\" — Lloyd co-founded");
    }

    [Fact]
    public void Prompt_never_contains_a_url()
    {
        var factCardPart = DraftPromptBuilder.Build(DraftLanguage.English, "guide", Card);

        factCardPart.ShouldNotContain("http");
        factCardPart.ShouldNotContain("www.");
    }
}
