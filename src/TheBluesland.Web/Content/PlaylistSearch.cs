using System.Text;
using TheBluesland.Web.Localization;

namespace TheBluesland.Web.Content;

/// <summary>
/// Searches visitor-facing playlist text and editorial tags, ranking partial matches by the number
/// of distinct query terms found. Equal scores retain the catalogue's existing order.
/// </summary>
public static class PlaylistSearch
{
    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> playlists,
        string? query,
        string languageCode = "en")
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
                Score: Tokenize(SearchableText(playlist, languageCode)).Count(queryTerms.Contains)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Playlist)
            .ToList();
    }

    // On a Turkish page the Turkish tag labels are searchable too ("melankolik", "caz"), next to the
    // raw tag values and the English title/summary that every language searches.
    private static string SearchableText(PlaylistContent playlist, string languageCode)
    {
        var tags = PlaylistTags.All(playlist);
        return string.Join(
            ' ',
            new[] { playlist.Title, playlist.Summary }
                .Concat(tags)
                .Concat(languageCode == SiteLanguage.Turkish
                    ? tags.Select(tag => TaxonomyLabels.Label(tag, languageCode))
                    : []));
    }

    // Case folding that survives the Turkish dotted/dotless i: "SICAK", "MELANKOLİK" and "İndie" must
    // match "sıcak", "melankolik" and "indie". char.ToLowerInvariant alone turns "I" into "i" (so
    // "SICAK" would never meet "sıcak") and leaves "İ" untouched. Every i variant folds to plain
    // "i" on both the query and the searchable text, in either language.
    private static char Fold(char character) =>
        character is 'I' or 'İ' or 'ı' ? 'i' : char.ToLowerInvariant(character);

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
                token.Append(Fold(character));
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
