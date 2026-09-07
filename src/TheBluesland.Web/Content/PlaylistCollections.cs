namespace TheBluesland.Web.Content;

/// <summary>Small, named entry points into the existing editorial tags; not arbitrary filter URLs.</summary>
public static class PlaylistCollections
{
    public static readonly IReadOnlyList<PlaylistCollection> All =
    [
        new("anadolu-rock", "Anadolu Rock Playlists",
            "Anatolian folk melodies meet electric guitars. Explore Anadolu rock through Mehmet's Spotify playlists.",
            "Fuzz guitar, folk melodies, and Turkish voices. Explore the Anadolu rock collection, from playlists devoted to a single artist to mixes that bring different sides of the sound together.",
            new PlaylistFilterCriteria([], ["anadolu-rock"], [], [])),
        new("blues", "Blues Playlists",
            "Acoustic blues, electric guitar and soulful voices. Explore the blues and blues-rock playlists at the heart of TheBluesland.",
            "Start at the heart of TheBluesland: blues in its many forms. Follow an acoustic guitar, settle into a soulful vocal, or turn to a full electric band. These playlists offer different ways into the music.",
            new PlaylistFilterCriteria([], ["blues", "blues-rock"], [], [])),
        new("late-night", "Late-Night Playlists",
            "Music for the hours after dark. Find late-night Spotify playlists across blues, jazz, folk and more.",
            "For a quiet room, a late drive, or a little more time with your headphones. These playlists take different routes through the night, from gentle and reflective to darker, more restless sounds.",
            new PlaylistFilterCriteria([], [], ["late-night"], [])),
    ];

    public static PlaylistCollection? Find(string slug) =>
        All.FirstOrDefault(collection => string.Equals(collection.Slug, slug, StringComparison.Ordinal));
}

public sealed record PlaylistCollection(
    string Slug,
    string Title,
    string Description,
    string Introduction,
    PlaylistFilterCriteria Criteria);
