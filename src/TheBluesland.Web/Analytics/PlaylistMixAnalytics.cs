namespace TheBluesland.Web.Analytics;

/// <summary>
/// Converts validated playlist mix URL state into a bounded analytics event type. Only the fixed
/// mode/action values are stored; raw query strings are never written to analytics.
/// </summary>
public static class PlaylistMixAnalytics
{
    public const string EventTypePrefix = "playlist_mix_";

    public static bool TryClassify(
        string path,
        string? modeToken,
        string? selectedPlaylistSlug,
        string? hiddenToken,
        string? previewToken,
        out string eventType)
    {
        eventType = string.Empty;
        if (!PageViewRoute.TryClassify(path, out var playlistSlug)
            || playlistSlug is null
            || !TryNormalizeMode(modeToken, out var normalizedMode))
        {
            return false;
        }

        var hasSelection = !string.IsNullOrEmpty(selectedPlaylistSlug);
        var isHidden = string.Equals(hiddenToken, "1", StringComparison.Ordinal);
        var isPreview = string.Equals(previewToken, "1", StringComparison.Ordinal);
        if (hasSelection
            && (!IsValidSlug(selectedPlaylistSlug!)
                || string.Equals(selectedPlaylistSlug, playlistSlug, StringComparison.Ordinal)))
        {
            return false;
        }

        if ((!string.IsNullOrEmpty(hiddenToken) && !isHidden)
            || (!string.IsNullOrEmpty(previewToken) && !isPreview)
            || (isHidden && isPreview))
        {
            return false;
        }

        var action = "applied";
        if (isHidden)
        {
            action = "dismissed";
        }
        else if (isPreview || !hasSelection)
        {
            action = "preview";
        }

        eventType = $"{EventTypePrefix}{action}_{normalizedMode}";
        return true;
    }

    private static bool TryNormalizeMode(string? value, out string normalizedMode)
    {
        normalizedMode = value?.Trim().ToLowerInvariant() switch
        {
            "same-vibe" => "same_vibe",
            "more-energetic" => "more_energetic",
            "relaxed-flow" => "relaxed_flow",
            "deeper-cuts" => "deeper_cuts",
            _ => string.Empty,
        };
        return normalizedMode.Length > 0;
    }

    private static bool IsValidSlug(string slug)
    {
        if (slug.Length is 0 or > 100 || !char.IsAsciiLetterOrDigit(slug[0]) || !char.IsAsciiLetterOrDigit(slug[^1]))
        {
            return false;
        }

        return slug.All(character =>
            char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character)
            || character == '-');
    }
}
