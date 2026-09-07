namespace TheBluesland.Web.Content;

public static class PlaylistEraAssignment
{
    private const string MixedEra = "mixed-era";

    public static PlaylistContent Apply(
        PlaylistContent playlist,
        IReadOnlyDictionary<string, string[]> computedEras)
    {
        if (playlist.Eras.Count != 1 || playlist.Eras[0] != MixedEra
            || !computedEras.TryGetValue(playlist.SpotifyPlaylistId, out var eras)
            || eras.Length == 0 || eras.Any(era => !PlaylistTaxonomy.Eras.Contains(era)))
        {
            return playlist;
        }

        return playlist with { Eras = eras.ToArray() };
    }
}
