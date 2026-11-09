using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Scripts;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API's Booking draft operation (ADR 0040): the checks of the other operations in the same order (subscription key,
/// token, <c>TransportOrders.Draft</c>, a Shipper for the <c>azp</c>, the rate limit), then the guards, then the Intake agent. The draft is
/// stateless, and neither the text nor the draft reaches the log.
/// </summary>
public sealed class CreateBookingDraftTests : IAsyncLifetime
{
    private static readonly string[] DraftRole = ["TransportOrders.Draft"];

    private readonly LocalPartnerApiFactory _factory = new();
    private HttpClient _partnerApi = null!;

    public ValueTask InitializeAsync()
    {
        _factory.AgentsEnabled = true;
        _factory.DraftsPerMinute = 2;
        _partnerApi = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _partnerApi.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Draft_AGoodEmail_AnswersADraftWithItsQuotes_AndNeverCallsTheTms()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body["booking"]!["bookingRef"]!.GetValue<string>().Should().Be("SH-2030-0142");
        body["quotes"]!["bookingRef"]!.GetValue<string>().Should().Be("SH-2030-0142");
        body["missing"]!.AsArray().Should().BeEmpty();
        _factory.Tms.Calls.Should().BeEmpty();
        response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle();
    }

    [Fact]
    public async Task Draft_APartialEmail_FlagsWhatIsMissing()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Partial);

        var missing = (await BodyAsync(response))["missing"]!.AsArray().Select(field => field!["field"]!.GetValue<string>());
        missing.Should().Contain(["bookingRef", "items.0.weight"]);
    }

    [Fact]
    public async Task Draft_WithoutASubscriptionKey_IsRefused401()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good, subscriptionKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Draft_WithAnInvalidToken_IsRefused401()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good, token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Draft_AsAClientWithoutTheDraftRole_IsRefused403_InThePolicysWords()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good, token: _factory.PersonaToken(roles: ["TransportOrders.Submit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not draft bookings from text.");
    }

    [Fact]
    public async Task Draft_AsAClientThatIsNoShipper_IsRefused403()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good, token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: DraftRole));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Shipper is registered for this client.");
    }

    [Fact]
    public async Task Draft_AsACarrier_IsRefused403()
    {
        using var response = await DraftAsync(
            BookingIntakeSamples.Good,
            subscriptionKey: LocalPartnerApiFactory.VanDijkKey,
            token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: DraftRole));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Draft_OverTheRateLimit_IsRefused429_WithRetryAfter()
    {
        (await DraftAsync(BookingIntakeSamples.Good)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DraftAsync(BookingIntakeSamples.Good)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var third = await DraftAsync(BookingIntakeSamples.Good);

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Draft_ARefusalAtTheEdge_IsNotCountedAgainstTheRateLimit()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await DraftAsync(BookingIntakeSamples.Good, token: "not-a-token")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await DraftAsync(BookingIntakeSamples.Good)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Draft_TextOverTheLimit_IsRefused413()
    {
        using var response = await DraftAsync(new string('x', 8001));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await BodyAsync(response))["failureCode"]!.GetValue<string>().Should().Be("InputTooLarge");
    }

    [Theory]
    [InlineData("""{"other": 1}""")]
    [InlineData("""{"text": "   "}""")]
    [InlineData("not json")]
    public async Task Draft_WithoutText_IsRefused400(string body)
    {
        using var response = await DraftRawAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(response))["failureCode"]!.GetValue<string>().Should().Be("TextRequired");
    }

    [Fact]
    public async Task Draft_TextWithTwoBookings_IsRefused422_AsMultipleBookingsInText()
    {
        using var response = await DraftAsync(BookingIntakeSamples.TwoBookings);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BodyAsync(response))["failureCode"]!.GetValue<string>().Should().Be("MultipleBookingsInText");
    }

    [Fact]
    public async Task Draft_WhenTheAgentsAreNotEnabled_IsRefused503_AndUsesNoRateLimit()
    {
        await using var off = new LocalPartnerApiFactory { AgentsEnabled = false, DraftsPerMinute = 1 };
        using var client = off.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await DraftAsync(client, off, BookingIntakeSamples.Good);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await BodyAsync(response))["failureCode"]!.GetValue<string>().Should().Be("AgentsDisabled");
        }
    }

    [Fact]
    public async Task Draft_NeitherTheTextNorTheDraftIsLogged()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good);
        _ = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var logged = string.Join('\n', _factory.Logs.GetSnapshot().Select(log => log.Message + string.Join(' ', log.Scopes.Select(scope => scope?.ToString()))));
        logged.Should().NotContain("Mueller").And.NotContain("Havenstraat").And.NotContain("SH-2030-0142");
    }

    [Fact]
    public async Task Draft_RecordsTheTrackingKeysOfTheFlow()
    {
        using var response = await DraftAsync(BookingIntakeSamples.Good);

        var keys = _factory.Logs.GetSnapshot()
            .Where(log => log.Category == typeof(CreateBookingDraft).FullName)
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(pairs => pairs).ToDictionary())
            .Last(pairs => pairs.TryGetValue("outcome", out var outcome) && Equals(outcome, "Succeeded"));
        keys.Should().Contain("flow", "booking-draft").And.Contain("partyId", "SHP-0001");
        keys["correlationId"].Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
    }

    private Task<HttpResponseMessage> DraftAsync(
        string text, string? subscriptionKey = LocalPartnerApiFactory.BrouwerKey, string token = "") =>
        DraftAsync(_partnerApi, _factory, text, subscriptionKey, token);

    private static async Task<HttpResponseMessage> DraftAsync(
        HttpClient client, LocalPartnerApiFactory factory, string text, string? subscriptionKey = LocalPartnerApiFactory.BrouwerKey, string token = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/booking-drafts")
        {
            Content = new StringContent(new JsonObject { ["text"] = text }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Length == 0 ? factory.PersonaToken(roles: DraftRole) : token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> DraftRawAsync(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/booking-drafts")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", LocalPartnerApiFactory.BrouwerKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.PersonaToken(roles: DraftRole));
        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
