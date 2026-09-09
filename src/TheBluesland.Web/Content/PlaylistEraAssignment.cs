using TheBluesland.Web.Cache;

namespace TheBluesland.Web.Content;

public static class PlaylistEraAssignment
{
    private const string MixedEra = "mixed-era";

    public static PlaylistContent Apply(
        PlaylistContent playlist,
        IReadOnlyDictionary<string, PlaylistCacheSignals> cacheSignals)
    {
        if (playlist.Eras.Count != 1 || playlist.Eras[0] != MixedEra
            || !cacheSignals.TryGetValue(playlist.SpotifyPlaylistId, out var signals)
            || signals.ComputedEras is not { Length: > 0 } eras
            || eras.Any(era => !PlaylistTaxonomy.Eras.Contains(era)))
        {
            return playlist;
        }

        return playlist with { Eras = eras.ToArray() };
    }
}
