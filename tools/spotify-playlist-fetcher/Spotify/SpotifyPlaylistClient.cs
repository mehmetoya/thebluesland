using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TheBluesland.SpotifyFetcher.EraReport;

namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Reads playlist-level facts from the Spotify Web API. Track-level data (title, id, duration,
/// ISRC) is read only transiently, one page at a time, while paginating the items endpoint
/// to compute distinct artists and era tags. Release years are kept only until the playlist's
/// aggregate has been calculated - see spec section 9.4 and 11.2. The <c>fields</c> query parameter narrows both
/// requests so Spotify itself never sends fields this tool has no use for.
///
/// Spotify's February 2026 Web API migration removed <c>GET /playlists/{id}/tracks</c> in favour
/// of <c>GET /playlists/{id}/items</c>, and the per-item track payload moved from the <c>track</c>
/// field (now present but always empty, <c>{}</c>) to <c>item</c>; the "Get Playlist" summary
/// endpoint's <c>tracks</c> container was renamed to <c>items</c> at the same time (its inner
/// shape is unchanged). This client reads the new <c>items</c>/<c>item</c> fields accordingly. The
/// new items endpoint additionally requires the <c>playlist-read-private</c> scope on the access
/// token, which must already be present on <c>SPOTIFY_REFRESH_TOKEN</c> from the interactive
/// authorization step (see <see cref="SpotifyAuthClient"/>).
/// </summary>
public sealed class SpotifyPlaylistClient
{
    private const string BaseUrl = "https://api.spotify.com/v1";

    // US-023/US-024 quota guards - see GetTrackReleaseYearsAsync's "Sampled, not exhaustive" note
    // for why these exist (a full crawl locked the account out for ~19.5 hours). Three pages is
    // 300 tracks, ample to estimate a four-bucket distribution; raise it only alongside a real
    // check of how many requests the whole 120-playlist run would then make. The delay applies to
    // every paginated track read, the sync's included: it is the unbroken burst of back-to-back
    // requests, not the total, that Spotify's limiter reacts to hardest.
    private const int MaxReleaseYearPages = 3;
    private static readonly TimeSpan TrackPageDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxRateLimitAttempts = 5;
    private static readonly TimeSpan MaximumRateLimitWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRateLimitRetryDelay = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _rateLimitRetryDelay;

    public SpotifyPlaylistClient(HttpClient httpClient, TimeSpan? rateLimitRetryDelay = null)
    {
        _httpClient = httpClient;
        _rateLimitRetryDelay = rateLimitRetryDelay ?? DefaultRateLimitRetryDelay;
    }

    /// <summary>
    /// Reads one playlist. The playlist-level summary is a single request; deriving
    /// <see cref="SpotifyPlaylistSummary.Artists"/> and <see cref="SpotifyPlaylistSummary.ComputedEras"/>
    /// then costs one request per 100 tracks, which is where a sync run's entire quota goes -
    /// Bluesland alone is 1601 tracks, `psychedelia` 10,000.
    ///
    /// <para>US-024: when <paramref name="knownSnapshotId"/> equals the snapshot id Spotify just
    /// returned, the playlist's tracks are byte-for-byte what they were at the caller's last
    /// successful read, so that paginated pass is skipped entirely and the result is flagged
    /// <see cref="SpotifyPlaylistFetchResult.Found.TrackAggregatesSkipped"/>. Pass <c>null</c> to
    /// force the full read - which the caller must do whenever it has no aggregates worth keeping,
    /// since a skipped fetch returns none.</para>
    /// </summary>
    public async Task<SpotifyPlaylistFetchResult> FetchAsync(
        string spotifyPlaylistId,
        string accessToken,
        string? knownSnapshotId,
        CancellationToken cancellationToken)
    {
        var summary = await GetPlaylistSummaryAsync(spotifyPlaylistId, accessToken, cancellationToken);
        if (summary is null)
        {
            return new SpotifyPlaylistFetchResult.NotFound();
        }

        // Ordinal, and only when both sides actually have a value: a null on either side means
        // "unknown", never "unchanged", so it must fall through to the full read.
        if (knownSnapshotId is { Length: > 0 }
            && summary.SnapshotId is { Length: > 0 } currentSnapshotId
            && string.Equals(knownSnapshotId, currentSnapshotId, StringComparison.Ordinal))
        {
            return new SpotifyPlaylistFetchResult.Found(summary, TrackAggregatesSkipped: true);
        }

        var (artists, eras) = await GetTrackAggregatesAsync(spotifyPlaylistId, accessToken, cancellationToken);
        return new SpotifyPlaylistFetchResult.Found(summary with { Artists = artists, ComputedEras = eras });
    }

    private async Task<SpotifyPlaylistSummary?> GetPlaylistSummaryAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}" +
                  "?fields=name,description,images,items.total,snapshot_id";

        using var response = await SendAsync(HttpMethod.Get, url, accessToken, cancellationToken);
        if (IsPlaylistUnavailableStatus(response.StatusCode))
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        if (name is null)
        {
            throw new InvalidOperationException(
                $"Spotify playlist response for '{spotifyPlaylistId}' did not include a 'name'.");
        }

        var description = root.TryGetProperty("description", out var descriptionElement)
            && descriptionElement.ValueKind != JsonValueKind.Null
                ? descriptionElement.GetString()
                : null;

        string? coverImageUrl = null;
        if (root.TryGetProperty("images", out var imagesElement)
            && imagesElement.ValueKind == JsonValueKind.Array
            && imagesElement.GetArrayLength() > 0)
        {
            coverImageUrl = imagesElement[0].TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;
        }

        var trackCount = root.TryGetProperty("items", out var itemsTotalElement)
            && itemsTotalElement.TryGetProperty("total", out var totalElement)
                ? totalElement.GetInt32()
                : 0;

        var snapshotId = root.TryGetProperty("snapshot_id", out var snapshotElement)
            && snapshotElement.ValueKind != JsonValueKind.Null
                ? snapshotElement.GetString()
                : null;

        return new SpotifyPlaylistSummary
        {
            Name = name,
            Description = description,
            CoverImageUrl = coverImageUrl,
            TrackCount = trackCount,
            SnapshotId = snapshotId,
            Artists = [],
        };
    }

    private async Task<(string[] Artists, string[] Eras)> GetTrackAggregatesAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var artistNames = new SortedSet<string>(StringComparer.Ordinal);
        var releaseYears = new List<int>();
        string? nextUrl = $"{BaseUrl}/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}/items" +
                           "?fields=items(item(artists(name),album(release_date))),next&limit=100";

        while (nextUrl is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, nextUrl, accessToken, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pageItem in itemsElement.EnumerateArray())
                {
                    CollectArtistNames(pageItem, artistNames);
                    if (TryReadReleaseYear(pageItem, out var year))
                    {
                        releaseYears.Add(year);
                    }
                }
            }

            nextUrl = root.TryGetProperty("next", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null
                ? nextElement.GetString()
                : null;

            // Only playlists whose snapshot id actually moved get this far (US-024), so the added
            // wall time is paid by a handful of playlists a month, not by all 120.
            if (nextUrl is not null)
            {
                await Task.Delay(TrackPageDelay, cancellationToken);
            }
        }

        var distribution = new PlaylistEraDistributionCalculator().Calculate(releaseYears);
        return ([.. artistNames], [.. distribution.SuggestedEras]);
    }

    /// <summary>
    /// US-023: reads only the <c>album.release_date</c> field for every track/episode in the
    /// playlist, for the sole purpose of computing an era distribution in memory (see
    /// <c>EraReport.PlaylistEraDistributionCalculator</c>). The <c>fields</c> parameter narrows
    /// the response to exactly that one nested field - no track title/id/duration/ISRC is ever
    /// requested here, matching spec section 9.4/11.2's "no track-level data is persisted" limit;
    /// the returned years are the only thing a caller may retain, and even those must never be
    /// written anywhere except an aggregated percentage. Release dates with month or day
    /// precision (<c>"1978-05"</c>, <c>"1978-05-12"</c>) are reduced to their leading year;
    /// episodes/local files with no album, and tracks with a missing or empty
    /// <c>release_date</c>, are skipped rather than counted as year zero.
    ///
    /// <para><b>Sampled, not exhaustive.</b> Reading at most <see cref="MaxReleaseYearPages"/>
    /// pages is a deliberate quota decision, not an oversight. The first real run against this
    /// catalogue (2026-09-07) followed <c>next</c> to exhaustion and got the entire Spotify
    /// account rate-limited for ~19.5 hours: `psychedelia` alone is 10,000 tracks (100 requests)
    /// and `no-more-words` 8,656, several hundred requests across the 120 playlists. An era
    /// *distribution* is a proportion, so a few hundred tracks estimate it perfectly well; the
    /// returned <see cref="TrackReleaseYearSample.WasSampled"/> tells the report which playlists
    /// were cut short so a reader can weigh the suggestion accordingly. Playlists shorter than the
    /// cap are still read in full.</para>
    /// </summary>
    public async Task<TrackReleaseYearSample> GetTrackReleaseYearsAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var releaseYears = new List<int>();
        string? nextUrl = $"{BaseUrl}/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}/items" +
                           "?fields=items(item(album(release_date))),next&limit=100";

        var pagesRead = 0;
        while (nextUrl is not null)
        {
            if (pagesRead == MaxReleaseYearPages)
            {
                // More pages exist but the cap stops here - report it as a sample.
                return new TrackReleaseYearSample(releaseYears, WasSampled: true);
            }

            using var response = await SendAsync(HttpMethod.Get, nextUrl, accessToken, cancellationToken);
            response.EnsureSuccessStatusCode();
            pagesRead++;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pageItem in itemsElement.EnumerateArray())
                {
                    if (TryReadReleaseYear(pageItem, out var year))
                    {
                        releaseYears.Add(year);
                    }
                }
            }

            nextUrl = root.TryGetProperty("next", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null
                ? nextElement.GetString()
                : null;

            // Spread the burst. The report walks 120 playlists back to back, and it was exactly
            // that unbroken stream of requests that tripped Spotify's limiter hard enough to lock
            // the account out for the best part of a day.
            if (nextUrl is not null)
            {
                await Task.Delay(TrackPageDelay, cancellationToken);
            }
        }

        return new TrackReleaseYearSample(releaseYears, WasSampled: false);
    }

    private static bool TryReadReleaseYear(JsonElement pageItem, out int year)
    {
        year = 0;

        // Same February 2026 shape as CollectArtistNames: the real payload is under "item", not
        // the deprecated always-empty "track".
        if (!pageItem.TryGetProperty("item", out var itemDetailElement)
            || itemDetailElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!itemDetailElement.TryGetProperty("album", out var albumElement)
            || albumElement.ValueKind != JsonValueKind.Object)
        {
            return false; // podcast episodes and some local files have no album at all
        }

        if (!albumElement.TryGetProperty("release_date", out var releaseDateElement)
            || releaseDateElement.GetString() is not { Length: >= 4 } releaseDate)
        {
            return false; // missing/empty release_date - do not count it as a dated track
        }

        var yearText = releaseDate.Split('-')[0];
        return int.TryParse(yearText, out year) && year > 0;
    }

    private static void CollectArtistNames(JsonElement pageItem, SortedSet<string> artistNames)
    {
        // Since the February 2026 API migration, "track" is present on every page item but is
        // always an empty, deprecated object ({}); the actual track/episode payload is under
        // "item" instead - reading "track" here would silently collect zero artists.
        if (!pageItem.TryGetProperty("item", out var itemDetailElement)
            || itemDetailElement.ValueKind != JsonValueKind.Object)
        {
            return; // Spotify returns a null item for removed/local items; nothing to attribute.
        }

        if (!itemDetailElement.TryGetProperty("artists", out var artistsElement)
            || artistsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var artist in artistsElement.EnumerateArray())
        {
            if (artist.TryGetProperty("name", out var artistNameElement)
                && artistNameElement.GetString() is { Length: > 0 } artistName)
            {
                artistNames.Add(artistName);
            }
        }
    }

    private static bool IsPlaylistUnavailableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.NotFound;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRateLimitAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests
                || attempt == MaxRateLimitAttempts)
            {
                return response;
            }

            var retryDelay = GetRateLimitRetryDelay(response, attempt);
            response.Dispose();
            if (retryDelay > MaximumRateLimitWait)
            {
                throw new HttpRequestException(
                    $"Spotify rate limit requires waiting {retryDelay.TotalSeconds:F0} seconds, " +
                    $"above the {MaximumRateLimitWait.TotalSeconds:F0}-second retry limit. " +
                    "Run again after that cooldown; no early retry was sent.",
                    null,
                    HttpStatusCode.TooManyRequests);
            }

            Console.Error.WriteLine(
                $"Spotify 429: waiting {retryDelay.TotalSeconds:F0}s before attempt {attempt + 1}/{MaxRateLimitAttempts}.");
            await Task.Delay(retryDelay, cancellationToken);
        }

        throw new InvalidOperationException("Spotify rate-limit retry loop ended unexpectedly.");
    }

    private TimeSpan GetRateLimitRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return TimeSpan.FromTicks(_rateLimitRetryDelay.Ticks * attempt);
    }
}
