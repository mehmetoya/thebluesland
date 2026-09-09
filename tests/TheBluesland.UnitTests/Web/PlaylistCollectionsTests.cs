using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// Guards the 9 mood/era collection entries added alongside the original 3 (genre/occasion):
/// every mood and every era except <c>mixed-era</c> (an explicit escape valve, not a listening
/// context) must have exactly one collection whose <see cref="PlaylistFilterCriteria"/> selects
/// only that value - a copy-paste slug/criteria mismatch across 9 near-identical entries would
/// otherwise silently show the wrong playlists on a collection page.
/// </summary>
public sealed class PlaylistCollectionsTests
{
    [Fact]
    public void All_has_twelve_entries()
    {
        PlaylistCollections.All.Count.ShouldBe(12);
    }

    [Fact]
    public void No_slug_collides_with_another_entry()
    {
        PlaylistCollections.All.Select(collection => collection.Slug).Distinct().Count()
            .ShouldBe(PlaylistCollections.All.Count);
    }

    [Fact]
    public void Mixed_era_has_no_collection()
    {
        PlaylistCollections.All.ShouldNotContain(collection => collection.Slug == "mixed-era");
    }

    [Theory]
    [InlineData("melancholic")]
    [InlineData("warm")]
    [InlineData("energetic")]
    [InlineData("raw")]
    [InlineData("nostalgic")]
    public void Every_mood_has_exactly_one_collection_selecting_only_itself(string mood)
    {
        var matches = PlaylistCollections.All.Where(collection => collection.Slug == mood).ToList();
        matches.Count.ShouldBe(1);
        var collection = matches[0];
        collection.Dimension.ShouldBe(Dimension.Mood);
        collection.Criteria.Moods.ShouldBe([mood]);
        collection.Criteria.Genres.ShouldBeEmpty();
        collection.Criteria.Occasions.ShouldBeEmpty();
        collection.Criteria.Eras.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("pre-1970")]
    [InlineData("1970s")]
    [InlineData("1980s-1990s")]
    [InlineData("2000s-present")]
    public void Every_non_mixed_era_has_exactly_one_collection_selecting_only_itself(string era)
    {
        var matches = PlaylistCollections.All.Where(collection => collection.Slug == era).ToList();
        matches.Count.ShouldBe(1);
        var collection = matches[0];
        collection.Dimension.ShouldBe(Dimension.Era);
        collection.Criteria.Eras.ShouldBe([era]);
        collection.Criteria.Moods.ShouldBeEmpty();
        collection.Criteria.Genres.ShouldBeEmpty();
        collection.Criteria.Occasions.ShouldBeEmpty();
    }

    [Fact]
    public void Existing_entries_keep_their_explicit_dimension()
    {
        PlaylistCollections.Find("anadolu-rock")!.Dimension.ShouldBe(Dimension.Genre);
        PlaylistCollections.Find("blues")!.Dimension.ShouldBe(Dimension.Genre);
        PlaylistCollections.Find("late-night")!.Dimension.ShouldBe(Dimension.Occasion);
    }
}
