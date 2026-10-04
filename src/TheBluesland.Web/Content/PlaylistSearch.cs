using System.Text;

namespace TheBluesland.Web.Content;

/// <summary>
/// Searches visitor-facing playlist text and editorial tags, ranking partial matches by the number
/// of distinct query terms found. Equal scores retain the catalogue's existing order.
/// </summary>
public static class PlaylistSearch
{
    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> playlists,
        string? query)
    {
        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
        {
            return playlists;
        }

        return playlists
            .Select((playlist, index) => (
                Playlist: playlist,
                Index: index,
                Score: Tokenize(SearchableText(playlist)).Count(queryTerms.Contains)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Playlist)
            .ToList();
    }

    private static string SearchableText(PlaylistContent playlist) =>
        string.Join(
            ' ',
            new[] { playlist.Title, playlist.Summary }
                .Concat(playlist.Moods)
                .Concat(playlist.Genres)
                .Concat(playlist.Occasions)
                .Concat(playlist.Eras));

    private static HashSet<string> Tokenize(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        var token = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                token.Append(char.ToLowerInvariant(character));
            }
            else if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            tokens.Add(token.ToString());
        }

        return tokens;
    }
}
