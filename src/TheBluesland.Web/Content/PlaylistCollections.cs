namespace TheBluesland.Web.Content;

/// <summary>Small, named entry points into the existing editorial tags; not arbitrary filter URLs.</summary>
public static class PlaylistCollections
{
    public static readonly IReadOnlyList<PlaylistCollection> All =
    [
        new("anadolu-rock", "Anadolu Rock Playlists",
            "Explore Mehmet's Anadolu rock Spotify playlists, with original curator notes and links to listen.",
            "Start with the Anadolu rock corner of TheBluesland. This collection brings together playlists tagged with the genre, so you can explore this part of Mehmet's library without searching through the full catalogue. Open a playlist to read its curator note and find the Spotify link.",
            new PlaylistFilterCriteria([], ["anadolu-rock"], [], [])),
        new("blues", "Blues Playlists",
            "Discover hand-curated blues and blues-rock Spotify playlists, each introduced by a personal curator note.",
            "Blues is one of the starting points of TheBluesland. Here you can browse the library's blues and blues-rock playlists together, then follow the individual curator notes to choose a direction. The selections remain separate playlists: read their descriptions to find the listening experience you want.",
            new PlaylistFilterCriteria([], ["blues", "blues-rock"], [], [])),
        new("late-night", "Late-Night Playlists",
            "Find Spotify playlists for late-night listening across genres, with curator notes explaining each selection.",
            "Choose a listening occasion before choosing a genre. These playlists share the library's late-night tag, bringing different parts of the collection into one place. Their summaries and curator notes explain what makes each selection distinct; open one to read more and continue listening on Spotify.",
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
