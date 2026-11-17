using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API answers a Consignee's query by tracking code as API Management does (ADR 0052, ADR 0023): the Consignee tracking
/// product's subscription key and no token, a limit per client IP, the TMS as API Management's identity with the code in a header, one
/// answer for a code that is wrong, expired or replaced, and the Liquid map of what the TMS sent.
/// </summary>
public sealed class ConsigneeTrackingTests : IAsyncLifetime
{
    private const string Code = "0123456789ABCDEF";

    private const string Canonical = """
        {
          "transportOrderNumber": "TO-0000007",
          "collection": {"city": "Rotterdam", "countryCode": "NL"},
          "delivery": {"city": "Utrecht", "countryCode": "NL"},
          "deliveryWindow": {"earliest": "2026-09-25T16:15:00Z", "latest": "2026-09-25T20:15:00Z"},
          "trackingStatus": "InTransit",
          "eta": "2026-09-25T19:00:00Z",
          "lastUpdate": "2026-09-25T11:00:00Z",
          "progress": [
            {"type": "Collected", "eventTime": "2026-09-25T09:00:00Z", "place": {"city": "Rotterdam", "countryCode": "NL"}},
            {"type": "Departed", "eventTime": "2026-09-25T11:00:00Z", "place": {"city": "Rotterdam", "countryCode": "NL"}}
          ],
          "proofOfDelivery": null
        }
        """;

    private readonly LocalPartnerApiFactory _factory = new();
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.Tms.Answer(HttpStatusCode.OK, Canonical);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-persona-key")]
    public async Task Query_WithoutTheConsigneeProductsKey_IsRefused401_AndTheTmsIsNotCalled(string? key)
    {
        using var response = await GetAsync(Code, key);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_NeedsNoToken_AndSendsTheCodeToTheTmsInAHeaderNotTheUrl()
    {
        using var response = await GetAsync(Code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _factory.Tms.Calls.Single().Request;
        call.RequestUri!.ToString().Should().NotContain(Code);
        call.Headers.GetValues("X-Tracking-Code").Should().Equal(Code);
        call.Headers.Contains("X-Correlation-ID").Should().BeTrue();
        call.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task Query_MapsTheCanonicalViewToTheConsigneeFormat_WithoutTheTransportOrderNumber()
    {
        using var response = await GetAsync(Code);

        var body = await BodyAsync(response);
        body["status"]!.GetValue<string>().Should().Be("IN_TRANSIT");
        body["pickupCity"]!.GetValue<string>().Should().Be("Rotterdam");
        body["receiverCity"]!.GetValue<string>().Should().Be("Utrecht");
        body["eta"]!.GetValue<string>().Should().Be("2026-09-25T19:00:00Z");
        body["events"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).Should().Equal("COLLECTED", "DEPARTED");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("TO-0000007");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Query_ACodeTheTmsDoesNotKnow_IsOneFixedAnswer(HttpStatusCode tmsStatus)
    {
        _factory.Tms.Answer(tmsStatus, """{"code":"TrackingCodeNotFound","detail":"The tracking code is not known to the TMS."}""");

        using var unknown = await GetAsync(Code);
        using var malformed = await GetAsync(string.Empty);

        foreach (var response in new[] { unknown, malformed })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("""{"code":"TrackingCodeNotFound","text":"We can't find this shipment."}""");
        }
    }

    [Fact]
    public async Task Query_AfterTenCodesThatMatchedNothing_IsRateLimited_WithoutCallingTheTms()
    {
        _factory.Tms.Answer(HttpStatusCode.NotFound, "{}");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var miss = await GetAsync(Code);
            miss.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        var callsBefore = _factory.Tms.Calls.Count;
        using var limited = await GetAsync(Code);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _factory.Tms.Calls.Should().HaveCount(callsBefore);
    }

    [Fact]
    public async Task Query_AfterTheMissWindowPasses_WorksAgain()
    {
        _factory.Tms.Answer(HttpStatusCode.NotFound, "{}");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            (await GetAsync(Code)).Dispose();
        }

        _factory.Time.Advance(ConsigneeAbuseLimits.Window + TimeSpan.FromSeconds(1));
        _factory.Tms.Answer(HttpStatusCode.OK, Canonical);
        using var response = await GetAsync(Code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Query_MoreThanSixtyCallsAMinute_IsRateLimited()
    {
        for (var attempt = 0; attempt < ConsigneeAbuseLimits.Calls; attempt++)
        {
            (await GetAsync(Code)).Dispose();
        }

        using var limited = await GetAsync(Code);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Query_WhenTheTmsIsUnreachable_Is503_WithTheCorrelationId()
    {
        _factory.Tms.Fail(new HttpRequestException("down"));

        using var response = await GetAsync(Code);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Query_NeverLogsTheCode_AndNamesTheTransportOrderItResolvedTo()
    {
        using var hit = await GetAsync(Code);
        _factory.Tms.Answer(HttpStatusCode.NotFound, "{}");
        using var miss = await GetAsync("FEDCBA9876543210");

        var everything = _factory.Logs.GetSnapshot()
            .Select(log => log.Message + string.Join(';', log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).Select(pair => $"{pair.Key}={pair.Value}")))
            .ToList();
        everything.Should().NotContain(text => text.Contains(Code, StringComparison.Ordinal) || text.Contains("FEDCBA9876543210", StringComparison.Ordinal));
        everything.Should().Contain(text => text.Contains("transportOrderNumber=TO-0000007", StringComparison.Ordinal));
        everything.Should().Contain(text => text.Contains("transportOrderNumber=none", StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> GetAsync(string code, string? key = LocalPartnerApiFactory.ConsigneeKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/consignee/v1/view");
        if (key is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        }

        request.Headers.Add("X-Tracking-Code", code);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
}
