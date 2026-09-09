namespace TheBluesland.Web.Content;

/// <summary>Small, named entry points into the existing editorial tags; not arbitrary filter URLs.</summary>
public static class PlaylistCollections
{
    public static readonly IReadOnlyList<PlaylistCollection> All =
    [
        new("anadolu-rock", "Anadolu Rock Playlists",
            "Anatolian folk melodies meet electric guitars. Explore Anadolu rock through Mehmet's Spotify playlists.",
            "Fuzz guitar, folk melodies, and Turkish voices. Explore the Anadolu rock collection, from playlists devoted to a single artist to mixes that bring different sides of the sound together.",
            Dimension.Genre,
            new PlaylistFilterCriteria([], ["anadolu-rock"], [], [])),
        new("blues", "Blues Playlists",
            "Acoustic blues, electric guitar and soulful voices. Explore the blues and blues-rock playlists at the heart of TheBluesland.",
            "Start at the heart of TheBluesland: blues in its many forms. Follow an acoustic guitar, settle into a soulful vocal, or turn to a full electric band. These playlists offer different ways into the music.",
            Dimension.Genre,
            new PlaylistFilterCriteria([], ["blues", "blues-rock"], [], [])),
        new("late-night", "Late-Night Playlists",
            "Music for the hours after dark. Find late-night Spotify playlists across blues, jazz, folk and more.",
            "For a quiet room, a late drive, or a little more time with your headphones. These playlists take different routes through the night, from gentle and reflective to darker, more restless sounds.",
            Dimension.Occasion,
            new PlaylistFilterCriteria([], [], ["late-night"], [])),
        new("melancholic", "Melancholic Playlists",
            "Slower, reflective listening. Explore melancholic Spotify playlists at TheBluesland.",
            "For when the mood calls for something quieter and more inward. These playlists lean into a slower tempo, sparser arrangement, or simply a more reflective lyric - different routes to the same unhurried feeling.",
            Dimension.Mood,
            new PlaylistFilterCriteria(["melancholic"], [], [], [])),
        new("warm", "Warm Playlists",
            "Rich, inviting listening. Explore warm Spotify playlists at TheBluesland.",
            "For a sound that wraps around the room rather than cutting through it. These playlists favour a rounder tone, a familiar groove, or a voice that feels like company - different routes to the same easy warmth.",
            Dimension.Mood,
            new PlaylistFilterCriteria(["warm"], [], [], [])),
        new("energetic", "Energetic Playlists",
            "Upbeat, driving listening. Explore energetic Spotify playlists at TheBluesland.",
            "For when the moment calls for pace. These playlists lean into a faster tempo, a harder groove, or a voice that pushes rather than settles - different routes to the same forward motion.",
            Dimension.Mood,
            new PlaylistFilterCriteria(["energetic"], [], [], [])),
        new("raw", "Raw Playlists",
            "Unpolished, direct listening. Explore raw Spotify playlists at TheBluesland.",
            "For a sound with the edges left on. These playlists favour a rougher tone, a looser take, or a performance that feels caught in the room rather than smoothed over - different routes to the same directness.",
            Dimension.Mood,
            new PlaylistFilterCriteria(["raw"], [], [], [])),
        new("nostalgic", "Nostalgic Playlists",
            "Wistful, familiar listening. Explore nostalgic Spotify playlists at TheBluesland.",
            "For a sound that feels like it's calling back to somewhere. These playlists lean into a familiar melody, an older recording style, or a lyric that looks backward - different routes to the same wistful pull.",
            Dimension.Mood,
            new PlaylistFilterCriteria(["nostalgic"], [], [], [])),
        new("pre-1970", "Pre-1970s Playlists",
            "Music from before 1970. Explore pre-1970s Spotify playlists at TheBluesland.",
            "For the decades before 1970: early blues, the first wave of rock and roll, and the folk and soul that grew alongside them. These playlists offer different ways into that earlier sound.",
            Dimension.Era,
            new PlaylistFilterCriteria([], [], [], ["pre-1970"])),
        new("1970s", "1970s Playlists",
            "Music from the 1970s. Explore 1970s Spotify playlists at TheBluesland.",
            "For the decade that carried blues into rock, folk into singer-songwriter records, and soul into funk. These playlists offer different ways into that sound.",
            Dimension.Era,
            new PlaylistFilterCriteria([], [], [], ["1970s"])),
        new("1980s-1990s", "1980s-1990s Playlists",
            "Music from the 1980s and 1990s. Explore 1980s-1990s Spotify playlists at TheBluesland.",
            "For two decades of change: new production, new genres, and older sounds carried forward by a new generation. These playlists offer different ways into that stretch of music.",
            Dimension.Era,
            new PlaylistFilterCriteria([], [], [], ["1980s-1990s"])),
        new("2000s-present", "2000s-Present Playlists",
            "Music from 2000 to today. Explore 2000s-present Spotify playlists at TheBluesland.",
            "For the last two and a half decades: the sounds still being made and the older ones still being carried forward. These playlists offer different ways into that ongoing stretch of music.",
            Dimension.Era,
            new PlaylistFilterCriteria([], [], [], ["2000s-present"])),
    ];

    public static PlaylistCollection? Find(string slug) =>
        All.FirstOrDefault(collection => string.Equals(collection.Slug, slug, StringComparison.Ordinal));
}

public sealed record PlaylistCollection(
    string Slug,
    string Title,
    string Description,
    string Introduction,
    Dimension Dimension,
    PlaylistFilterCriteria Criteria);

/// <summary>The taxonomy dimension a collection represents - used only by <c>CollectionsPage</c> to
/// group the index; it carries no filtering logic, <see cref="PlaylistCollection.Criteria"/> still
/// does that.</summary>
public enum Dimension
{
    Genre,
    Occasion,
    Mood,
    Era,
}
