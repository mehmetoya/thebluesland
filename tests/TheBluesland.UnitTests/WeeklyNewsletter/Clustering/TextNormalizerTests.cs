using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>story-clustering spec, Design section 2, step 1.</summary>
public sealed class TextNormalizerTests
{
    private static readonly TextNormalizer Normalizer = new([new Abbreviation("grand theft auto", "gta"), new Abbreviation("p!nk", "pink")]);

    [Fact]
    public void Tokenize_lowercases_strips_punctuation_and_stop_words()
    {
        Normalizer.Tokenize("Oasis Won’t Attend Their Rock & Roll Hall Of Fame Induction")
            .ShouldBe(["oasis", "won", "t", "attend", "rock", "roll", "hall", "fame", "induction"]);
    }

    [Fact]
    public void Tokenize_removes_accents()
    {
        Normalizer.Tokenize("Céline Dion · Rosalía · Délicieuse Musique").ShouldBe(["celine", "dion", "rosalia", "delicieuse", "musique"]);
    }

    [Fact]
    public void Tokenize_drops_possessive_s_with_straight_or_curly_apostrophe()
    {
        Normalizer.Tokenize("Limp Bizkit’s Fred Durst and Slayer's Kerry King").ShouldBe(["limp", "bizkit", "fred", "durst", "slayer", "kerry", "king"]);
    }

    [Fact]
    public void Tokenize_applies_configured_abbreviations_before_punctuation_is_removed()
    {
        Normalizer.Tokenize("P!NK Announces 2027 Sphere Residency").ShouldBe(["pink", "announces", "2027", "sphere", "residency"]);
        Normalizer.Tokenize("Grand Theft Auto VI: The Festival").ShouldBe(["gta", "vi", "festival"]);
    }

    [Fact]
    public void Tokenize_applies_abbreviations_to_whole_words_only()
    {
        var normalizer = new TextNormalizer([new Abbreviation("rs", "record store")]);

        normalizer.Tokenize("RS day at Mars hall").ShouldBe(["record", "store", "day", "mars", "hall"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  — “” ‘’ ")]
    public void Tokenize_returns_nothing_for_empty_or_punctuation_only_text(string? text)
    {
        Normalizer.Tokenize(text).ShouldBeEmpty();
    }
}
