using System.Net;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Delivery;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>Kit's free plan has no form rules: confirmed form subscribers are copied into the delivery tag by API.</summary>
public sealed class KitSyncTagsCommandTests : IDisposable
{
    private const string Yaml = """
        timeZone: Europe/Istanbul
        minimumLeadMinutes: 2
        languages:
          tr:
            sendTime: "08:00"
            kitTagId: 111
            kitFormId: 901
          en:
            sendTime: "15:00"
            kitTagId: 222
        """;

    private readonly string _tool = Directory.CreateTempSubdirectory("kit-sync-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public KitSyncTagsCommandTests() => File.WriteAllText(Path.Combine(_tool, "delivery.yml"), Yaml);

    public void Dispose() => Directory.Delete(_tool, recursive: true);

    [Fact]
    public async Task Untagged_form_subscribers_are_tagged_and_only_counts_are_printed()
    {
        var handler = new QueueHandler(
            _ => Subscribers("""{"id":1,"email_address":"a@example.com"}"""),
            _ => Subscribers("""{"id":1,"email_address":"a@example.com"},{"id":2,"email_address":"b@example.com"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });

        (await RunAsync(handler, dryRun: false)).ShouldBe(0, _error.ToString());

        handler.Requests.Select(request => $"{request.Request.Method} {request.Request.RequestUri}").ShouldBe(
        [
            "GET https://api.kit.com/v4/tags/111/subscribers?status=active&per_page=1000",
            "GET https://api.kit.com/v4/forms/901/subscribers?status=active&per_page=1000",
            "POST https://api.kit.com/v4/tags/111/subscribers/2",
        ]);
        _output.ToString().ShouldContain("| tr | 901 | 111 | 1 tagged, 1 already |");
        _output.ToString().ShouldContain("| en | – | – | no kitFormId |");
        _output.ToString().ShouldNotContain("@example.com");
    }

    [Fact]
    public async Task A_dry_run_tags_nobody()
    {
        var handler = new QueueHandler(_ => Subscribers(string.Empty), _ => Subscribers("""{"id":7}"""));

        (await RunAsync(handler, dryRun: true)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldAllBe(request => request.Request.Method == HttpMethod.Get);
        _output.ToString().ShouldContain("1 to tag");
    }

    [Fact]
    public async Task A_kit_error_fails_without_the_key()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"errors":["The access token is invalid"]}""") });

        (await RunAsync(handler, dryRun: false)).ShouldBe(1);

        _error.ToString().ShouldContain("The access token is invalid");
        _error.ToString().ShouldNotContain("kit_test");
    }

    private Task<int> RunAsync(QueueHandler handler, bool dryRun) =>
        KitSyncTagsCommand.RunAsync(_tool, "kit_test", dryRun, new HttpClient(handler), _output, _error, CancellationToken.None, TimeSpan.Zero);

    private static HttpResponseMessage Subscribers(string items) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"subscribers":[""" + items + """],"pagination":{"has_next_page":false,"end_cursor":null}}"""),
    };
}
