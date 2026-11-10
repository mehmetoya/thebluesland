using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>A broadcast as Kit lists it; <see cref="Status"/> is draft, scheduled, sending, completed or aborted.</summary>
public sealed record KitBroadcast(
    long Id,
    string? Description,
    string? Subject,
    string? PreviewText,
    string? Content,
    DateTimeOffset? SendAt,
    string? Status,
    long? EmailTemplateId,
    long? TagId);

/// <summary>A Kit tag; the delivery lists are the tags "weekly-tr" and "weekly-en".</summary>
public sealed record KitTag(long Id, string Name);

/// <summary>What we ask Kit to send: one issue, to one tag, at one instant. No <c>SendAt</c> makes a draft that never sends (kit-smoke).</summary>
public sealed record KitBroadcastRequest(string Description, IssueEmail Email, DateTimeOffset? SendAt, long TagId, bool Public);

/// <summary>The non-secret facts of the Kit account that kit-smoke prints; any may be missing.</summary>
public sealed record KitAccount(string? Name, string? Plan, string? PrimaryEmail);

public sealed class KitException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Kit API v4 broadcasts (issue-delivery spec, Design sections 2-3): list, create, update, delete.
/// The key goes in the X-Kit-Api-Key header, never the URL. Kit's 429 carries no Retry-After, so
/// 429 and 5xx are retried with a doubling pause. Error text is Kit's "errors" array, never the key.
/// Creating a broadcast is the one call that must not be retried blindly: after a timeout or 5xx
/// Kit may have created it anyway, so <see cref="CreateAsync"/> looks for its marker first.
/// </summary>
public sealed class KitClient(HttpClient httpClient, string apiKey, TimeSpan? retryDelay = null)
{
    public const string ApiKeyVariable = "KIT_API_KEY";
    private const string BaseUrl = "https://api.kit.com/v4/broadcasts";
    private const string TagsUrl = "https://api.kit.com/v4/tags";
    private const string FormsUrl = "https://api.kit.com/v4/forms";
    private const string AccountUrl = "https://api.kit.com/v4/account";
    private const int MaxAttempts = 4;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<KitBroadcast>> ListAsync(CancellationToken cancellationToken)
    {
        var broadcasts = new List<KitBroadcast>();
        string? after = null;
        do
        {
            var url = after is null ? $"{BaseUrl}?per_page=1000" : $"{BaseUrl}?per_page=1000&after={Uri.EscapeDataString(after)}";
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<ListResponse>(Json, cancellationToken)
                ?? throw new KitException("Kit's broadcast list was empty.");
            broadcasts.AddRange((page.Broadcasts ?? []).Select(Map));
            after = page.Pagination is { HasNextPage: true, EndCursor: { Length: > 0 } cursor } ? cursor : null;
        }
        while (after is not null);

        return broadcasts;
    }

    public async Task<IReadOnlyList<KitTag>> ListTagsAsync(CancellationToken cancellationToken)
    {
        var tags = new List<KitTag>();
        string? after = null;
        do
        {
            var url = after is null ? $"{TagsUrl}?per_page=1000" : $"{TagsUrl}?per_page=1000&after={Uri.EscapeDataString(after)}";
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TagListResponse>(Json, cancellationToken)
                ?? throw new KitException("Kit's tag list was empty.");
            tags.AddRange((page.Tags ?? []).Where(tag => tag.Name is not null).Select(tag => new KitTag(tag.Id, tag.Name!)));
            after = page.Pagination is { HasNextPage: true, EndCursor: { Length: > 0 } cursor } ? cursor : null;
        }
        while (after is not null);

        return tags;
    }

    public async Task<KitTag> CreateTagAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, TagsUrl) { Content = JsonContent.Create(new { name }) }, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<TagResponse>(Json, cancellationToken);
        return created?.Tag is { Name: not null } tag ? new KitTag(tag.Id, tag.Name) : throw new KitException($"Kit did not return the tag '{name}'.");
    }

    /// <summary>Ids of a form's active (confirmed) subscribers. Only ids are read; e-mail addresses are never deserialized.</summary>
    public Task<IReadOnlyList<long>> ListFormSubscriberIdsAsync(long formId, CancellationToken cancellationToken) =>
        ListSubscriberIdsAsync($"{FormsUrl}/{formId}/subscribers", cancellationToken);

    /// <summary>Ids of a tag's active subscribers.</summary>
    public Task<IReadOnlyList<long>> ListTagSubscriberIdsAsync(long tagId, CancellationToken cancellationToken) =>
        ListSubscriberIdsAsync($"{TagsUrl}/{tagId}/subscribers", cancellationToken);

    public async Task TagSubscriberAsync(long tagId, long subscriberId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{TagsUrl}/{tagId}/subscribers/{subscriberId}") { Content = JsonContent.Create(new { }) }, cancellationToken);
    }

    private async Task<IReadOnlyList<long>> ListSubscriberIdsAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        string? after = null;
        do
        {
            var url = after is null ? $"{baseUrl}?status=active&per_page=1000" : $"{baseUrl}?status=active&per_page=1000&after={Uri.EscapeDataString(after)}";
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<SubscriberListResponse>(Json, cancellationToken)
                ?? throw new KitException("Kit's subscriber list was empty.");
            ids.AddRange((page.Subscribers ?? []).Select(subscriber => subscriber.Id));
            after = page.Pagination is { HasNextPage: true, EndCursor: { Length: > 0 } cursor } ? cursor : null;
        }
        while (after is not null);

        return ids;
    }

    /// <summary>
    /// Creates a broadcast. A timeout, network error or 5xx is ambiguous (Kit may have created it
    /// before failing), so a blind retry could schedule the same issue twice and the planner, seeing
    /// two markers, would do nothing and let both send. After such a failure the broadcasts are
    /// listed and one carrying the same description is taken as ours; only when none exists is the
    /// POST repeated. 429 is a clean refusal and is retried as usual.
    /// </summary>
    public async Task<long> CreateAsync(KitBroadcastRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, BaseUrl) { Content = Body(request, null) }, cancellationToken, retryAmbiguous: false);
                var created = await response.Content.ReadFromJsonAsync<SingleResponse>(Json, cancellationToken);
                return created?.Broadcast?.Id ?? throw new KitException("Kit did not return the new broadcast's id.");
            }
            catch (AmbiguousSendException ex)
            {
                await Task.Delay(_retryDelay * Math.Pow(2, attempt - 1), cancellationToken);
                var existing = (await ListAsync(cancellationToken))
                    .FirstOrDefault(broadcast => string.Equals(broadcast.Description?.Trim(), request.Description, StringComparison.Ordinal));
                if (existing is not null)
                {
                    return existing.Id;
                }

                if (attempt >= MaxAttempts)
                {
                    throw new KitException(ex.Message, ex.Status);
                }
            }
        }
    }

    public async Task<KitBroadcast> GetAsync(long id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{id}"), cancellationToken);
        var single = await response.Content.ReadFromJsonAsync<SingleResponse>(Json, cancellationToken);
        return single?.Broadcast is { } dto ? Map(dto) : throw new KitException($"Kit did not return broadcast {id}.");
    }

    /// <summary>The account's name, plan and primary (login) e-mail; only these fields are read.</summary>
    public async Task<KitAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, AccountUrl), cancellationToken);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(Json, cancellationToken);
        return new KitAccount(account?.Account?.Name, account?.Account?.PlanType, account?.Account?.PrimaryEmailAddress ?? account?.User?.Email);
    }

    public async Task UpdateAsync(long id, KitBroadcastRequest request, long? emailTemplateId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/{id}") { Content = Body(request, emailTemplateId) }, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/{id}"), cancellationToken);
    }

    private static JsonContent Body(KitBroadcastRequest request, long? emailTemplateId)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = request.Email.Html,
            ["description"] = request.Description,
            ["public"] = request.Public,
            ["published_at"] = request.SendAt?.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            ["send_at"] = request.SendAt?.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            ["preview_text"] = request.Email.PreviewText,
            ["subject"] = request.Email.Subject,
            ["email_address"] = null,
            ["thumbnail_alt"] = null,
            ["thumbnail_url"] = null,
            ["subscriber_filter"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["all"] = new[] { new { type = "tag", ids = new[] { request.TagId } } },
                    ["any"] = null,
                    ["none"] = null,
                },
            },
        };

        // Kit's default template when we have none; an update keeps the broadcast's own.
        if (emailTemplateId is { } template)
        {
            body["email_template_id"] = template;
        }

        return JsonContent.Create(body);
    }

    /// <summary>
    /// Sends with retries. With <paramref name="retryAmbiguous"/> off (a POST that creates something),
    /// a timeout, network error or 5xx is not retried but raised as <see cref="AmbiguousSendException"/>
    /// so the caller can check whether Kit acted before failing.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken, bool retryAmbiguous = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = build();
            request.Headers.Add("X-Kit-Api-Key", apiKey);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (!retryAmbiguous)
                {
                    throw new AmbiguousSendException($"Kit {request.Method} {request.RequestUri?.AbsolutePath} got no usable answer ({ex.GetType().Name}).", null);
                }

                if (attempt >= MaxAttempts)
                {
                    throw;
                }

                await Task.Delay(_retryDelay * Math.Pow(2, attempt - 1), cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var serverError = (int)response.StatusCode >= 500;
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests || serverError;
                if (!transient || attempt >= MaxAttempts || (serverError && !retryAmbiguous))
                {
                    var message = $"Kit {request.Method} {request.RequestUri?.AbsolutePath} failed with {(int)response.StatusCode} {response.StatusCode}{(retryAmbiguous ? $" (attempt {attempt} of {MaxAttempts})" : string.Empty)}{await ErrorDetailAsync(response, cancellationToken)}.";
                    throw serverError && !retryAmbiguous
                        ? new AmbiguousSendException(message, response.StatusCode)
                        : new KitException(message, response.StatusCode);
                }
            }

            await Task.Delay(_retryDelay * Math.Pow(2, attempt - 1), cancellationToken);
        }
    }

    /// <summary>A create that failed in a way that may still have created the broadcast in Kit.</summary>
    private sealed class AmbiguousSendException(string message, HttpStatusCode? status) : Exception(message)
    {
        public HttpStatusCode? Status { get; } = status;
    }

    private static async Task<string> ErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, cancellationToken);
            if (errors?.Errors is { Count: > 0 } list)
            {
                var flat = string.Join("; ", list).ReplaceLineEndings(" ");
                return $": {(flat.Length > 300 ? flat[..300] + "…" : flat)}";
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static KitBroadcast Map(BroadcastDto dto) => new(
        dto.Id,
        dto.Description,
        dto.Subject,
        dto.PreviewText,
        dto.Content,
        dto.SendAt,
        dto.Status,
        dto.EmailTemplate?.Id,
        dto.SubscriberFilter?.FirstOrDefault()?.All?.FirstOrDefault(filter => filter.Type == "tag")?.Ids?.FirstOrDefault());

    private sealed record ListResponse(List<BroadcastDto>? Broadcasts, Pagination? Pagination);

    private sealed record Pagination(bool HasNextPage, string? EndCursor);

    private sealed record AccountDto(string? Name, string? PlanType, string? PrimaryEmailAddress);

    private sealed record AccountUserDto(string? Email);

    private sealed record AccountResponse(AccountDto? Account, AccountUserDto? User);

    private sealed record SingleResponse(BroadcastDto? Broadcast);

    private sealed record ErrorResponse(List<string>? Errors);

    private sealed record TagDto(long Id, string? Name);

    private sealed record TagListResponse(List<TagDto>? Tags, Pagination? Pagination);

    private sealed record TagResponse(TagDto? Tag);

    private sealed record SubscriberIdDto(long Id);

    private sealed record SubscriberListResponse(List<SubscriberIdDto>? Subscribers, Pagination? Pagination);

    private sealed record BroadcastDto(
        long Id,
        string? Description,
        string? Subject,
        string? PreviewText,
        string? Content,
        DateTimeOffset? SendAt,
        string? Status,
        TemplateDto? EmailTemplate,
        List<FilterGroupDto>? SubscriberFilter);

    private sealed record TemplateDto(long? Id);

    private sealed record FilterGroupDto(List<FilterDto>? All);

    private sealed record FilterDto(string? Type, List<long>? Ids);
}
