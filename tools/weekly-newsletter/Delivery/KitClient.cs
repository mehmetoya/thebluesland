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

/// <summary>What we ask Kit to send: one issue, to one tag, at one instant.</summary>
public sealed record KitBroadcastRequest(string Description, IssueEmail Email, DateTimeOffset SendAt, long TagId, bool Public);

public sealed class KitException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Kit API v4 broadcasts (issue-delivery spec, Design sections 2-3): list, create, update, delete.
/// The key goes in the X-Kit-Api-Key header, never the URL. Kit's 429 carries no Retry-After, so
/// 429 and 5xx are retried with a doubling pause. Error text is Kit's "errors" array, never the key.
/// </summary>
public sealed class KitClient(HttpClient httpClient, string apiKey, TimeSpan? retryDelay = null)
{
    public const string ApiKeyVariable = "KIT_API_KEY";
    private const string BaseUrl = "https://api.kit.com/v4/broadcasts";
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

    public async Task<long> CreateAsync(KitBroadcastRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, BaseUrl) { Content = Body(request, null) }, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<SingleResponse>(Json, cancellationToken);
        return created?.Broadcast?.Id ?? throw new KitException("Kit did not return the new broadcast's id.");
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
            ["published_at"] = request.SendAt.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            ["send_at"] = request.SendAt.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
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

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
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
            catch (Exception ex) when (attempt < MaxAttempts
                && (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                await Task.Delay(_retryDelay * Math.Pow(2, attempt - 1), cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!transient || attempt >= MaxAttempts)
                {
                    throw new KitException(
                        $"Kit {request.Method} {request.RequestUri?.AbsolutePath} failed with {(int)response.StatusCode} {response.StatusCode} (attempt {attempt} of {MaxAttempts}){await ErrorDetailAsync(response, cancellationToken)}.",
                        response.StatusCode);
                }
            }

            await Task.Delay(_retryDelay * Math.Pow(2, attempt - 1), cancellationToken);
        }
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

    private sealed record SingleResponse(BroadcastDto? Broadcast);

    private sealed record ErrorResponse(List<string>? Errors);

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
