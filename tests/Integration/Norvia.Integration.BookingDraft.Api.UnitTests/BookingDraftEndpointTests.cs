using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.BookingDraft.Api.UnitTests;

/// <summary>The operation behind API Management: only the gateway calls it, the policy's headers are the trail's keys, and the text is never logged.</summary>
public sealed class BookingDraftEndpointTests : IAsyncLifetime
{
    private const string Text = "SECRET-CONSIGNEE-TEXT";

    private DraftHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await DraftHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static HttpRequestMessage Request(string? token, string body = $$"""{"text": "{{Text}}"}""", string? correlationId = "corr-1", string? shipper = "SHIPPER-1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BookingDraftEndpoint.Route) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        if (shipper is not null)
        {
            request.Headers.Add("X-Shipper-ID", shipper);
        }

        return request;
    }

    [Fact]
    public async Task The_gateway_gets_a_draft_with_its_correlation_ID_back()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-ID").Should().Equal("corr-1");
        (await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!.ContainsKey("booking").Should().BeTrue();
        _host.Agent.Asked.Should().ContainSingle().Which.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task A_PDF_is_read_for_its_text_and_the_agent_is_asked_with_it()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(Text, 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700),
            builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
        using var request = Request(_host.Token());
        request.Content = new ByteArrayContent(builder.Build());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var response = await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Agent.Asked.Should().ContainSingle().Which.Text.Should().Contain(Text);
        _host.Logs.GetSnapshot().Select(log => log.Message).Should().NotContain(message => message.Contains(Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_format_that_is_not_text_or_PDF_is_415_and_reaches_no_agent()
    {
        using var request = Request(_host.Token());
        request.Content = new ByteArrayContent([1, 2, 3]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!["failureCode"]!.GetValue<string>().Should().Be("UnsupportedFormat");
        _host.Agent.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_call_without_a_token_is_401_and_reaches_no_agent()
    {
        var response = await _host.Client.SendAsync(Request(token: null), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _host.Agent.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_token_without_the_role_is_403()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token(roles: ["Something.Else"])), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _host.Agent.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_token_with_the_role_that_is_not_the_gateways_is_403()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token(azp: "00000000-0000-0000-0000-0000000000ff")), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _host.Agent.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_token_for_another_audience_is_401()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token(audience: "api://another")), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_call_without_a_correlation_ID_starts_a_trail_of_its_own()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token(), correlationId: null), TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-ID").Single().Should().NotBeNullOrWhiteSpace().And.NotBe("corr-1");
    }

    [Fact]
    public async Task A_request_without_a_text_is_400_with_a_code()
    {
        var response = await _host.Client.SendAsync(Request(_host.Token(), body: "{}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!["failureCode"]!.GetValue<string>().Should().Be("TextRequired");
    }

    [Fact]
    public async Task The_trail_holds_the_keys_of_the_hop_and_never_the_text_or_the_draft()
    {
        await _host.Client.SendAsync(Request(_host.Token()), TestContext.Current.CancellationToken);

        var log = _host.Logs.GetSnapshot().Single(entry => entry.Message == "Answered with a Booking draft");
        var keys = log.Scopes.SelectMany(scope => (IEnumerable<KeyValuePair<string, object?>>)scope!).ToDictionary(pair => pair.Key, pair => pair.Value);
        keys["correlationId"].Should().Be("corr-1");
        keys["flow"].Should().Be("booking-draft");
        keys["partyId"].Should().Be("SHIPPER-1");
        keys["outcome"].Should().Be("Succeeded");
        _host.Logs.GetSnapshot().Should().NotContain(entry => entry.Message.Contains(Text, StringComparison.Ordinal) || entry.Message.Contains("booking\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_is_a_refused_hop_with_its_code()
    {
        await _host.Client.SendAsync(Request(_host.Token(), body: "not json"), TestContext.Current.CancellationToken);

        var log = _host.Logs.GetSnapshot().Single(entry => entry.Message.StartsWith("Refused with 400", StringComparison.Ordinal));
        var keys = log.Scopes.SelectMany(scope => (IEnumerable<KeyValuePair<string, object?>>)scope!).ToDictionary(pair => pair.Key, pair => pair.Value);
        keys["outcome"].Should().Be("Refused");
        keys["failureCode"].Should().Be("TextRequired");
    }

    [Fact]
    public async Task An_agent_that_fails_is_503_and_a_failed_hop()
    {
        await using var host = await DraftHost.StartAsync(reply: () => throw new InvalidOperationException("down"));

        var response = await host.Client.SendAsync(Request(host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var log = host.Logs.GetSnapshot().Single(entry => entry.Message.StartsWith("The assistant failed", StringComparison.Ordinal));
        log.Scopes.SelectMany(scope => (IEnumerable<KeyValuePair<string, object?>>)scope!).Should().Contain(pair => pair.Key == "outcome" && (string?)pair.Value == "Failed");
    }

    [Fact]
    public async Task A_text_over_the_limit_is_413()
    {
        await using var host = await DraftHost.StartAsync(maxCharacters: 100);

        var response = await host.Client.SendAsync(Request(host.Token(), body: $$"""{"text": "{{new string('x', 101)}}"}"""), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_switched_off_assistant_is_503_and_asks_no_agent()
    {
        await using var host = await DraftHost.StartAsync(enabled: false);

        var response = await host.Client.SendAsync(Request(host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!["failureCode"]!.GetValue<string>().Should().Be("AgentsDisabled");
        host.Agent.Asked.Should().BeEmpty();
    }
}
