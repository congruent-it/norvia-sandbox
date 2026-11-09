using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Norvia.Agents.Scripts;

namespace Norvia.Integration.BookingDraft.IntegrationTests;

/// <summary>
/// The local Partner API's Booking draft operation (ADR 0040) end to end through the real host: the edge's checks in API Management's order, then
/// the Intake agent. Each test builds a host of its own, with its own subscription key and rate limit counter, so none depends on another.
/// </summary>
public sealed class BookingDraftTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] WrongRole = ["TransportOrders.Submit"];

    [Fact]
    public async Task Draft_WithoutASubscriptionKey_IsRefused401()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, withoutSubscriptionKey: true);

        await AssertRefusedAsync(response, HttpStatusCode.Unauthorized, "missing subscription key");
    }

    [Fact]
    public async Task Draft_WithAWrongSubscriptionKey_IsRefused401()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, subscriptionKey: "not-the-key");

        await AssertRefusedAsync(response, HttpStatusCode.Unauthorized, "invalid subscription key");
    }

    [Fact]
    public async Task Draft_WithoutAToken_IsRefused401()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, withoutToken: true);

        await AssertRefusedAsync(response, HttpStatusCode.Unauthorized, "Access token is missing or invalid");
    }

    [Fact]
    public async Task Draft_WithATokenSignedByAnotherKey_IsRefused401()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, token: host.PersonaToken(signingKey: new byte[64]));

        await AssertRefusedAsync(response, HttpStatusCode.Unauthorized, "Access token is missing or invalid");
    }

    [Fact]
    public async Task Draft_WithATokenWithoutTheDraftRole_IsRefused403()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, token: host.PersonaToken(roles: WrongRole));

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "may not draft bookings from text");
    }

    [Fact]
    public async Task Draft_WithATokenWhoseClientIsNoShipper_IsRefused403()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, token: host.PersonaToken(clientId: "norvia-local-persona-stranger"));

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "No Shipper is registered for this client");
    }

    [Fact]
    public async Task Draft_OverTheRateLimit_IsRefused429_WithRetryAfter()
    {
        await using var host = new DraftApiHost(draftsPerMinute: 2);
        await DraftStatusAsync(host);
        await DraftStatusAsync(host);

        using var third = await host.DraftAsync(BookingIntakeSamples.Good);

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.Should().NotBeNull();
    }

    // Each failure is checked next to a later one that is also present, so the order of the edge's checks shows.
    [Fact]
    public async Task Draft_WithSeveralFaults_IsRefusedForTheFirstInTheEdgesOrder()
    {
        await using var host = new DraftApiHost(draftsPerMinute: 1);
        (await DraftStatusAsync(host)).Should().Be(HttpStatusCode.OK, "the rate limit is used up");
        var stranger = host.PersonaToken(clientId: "norvia-local-persona-stranger", roles: WrongRole);

        // Key before token, token before role, role before Shipper, Shipper before rate limit.
        await AssertRefusedAsync(await host.DraftAsync(BookingIntakeSamples.Good, subscriptionKey: "no", token: "no"), HttpStatusCode.Unauthorized, "invalid subscription key");
        await AssertRefusedAsync(await host.DraftAsync(BookingIntakeSamples.Good, token: "no"), HttpStatusCode.Unauthorized, "Access token is missing or invalid");
        await AssertRefusedAsync(await host.DraftAsync(BookingIntakeSamples.Good, token: stranger), HttpStatusCode.Forbidden, "may not draft bookings from text");
        await AssertRefusedAsync(
            await host.DraftAsync(BookingIntakeSamples.Good, token: host.PersonaToken(clientId: "norvia-local-persona-stranger")),
            HttpStatusCode.Forbidden, "No Shipper is registered for this client");
        (await host.DraftAsync(BookingIntakeSamples.Good)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Draft_AGoodText_GivesADraftThatPassesTheSchema_AndEveryValueHasAQuoteFromTheText()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await BodyAsync(response);
        BookingDraftSchema.Errors(draft).Should().BeEmpty("the booking part and the draft around it follow the contract");
        draft["missing"]!.AsArray().Should().BeEmpty();
        var quotes = draft["quotes"]!.AsObject();
        ValuePaths(draft["booking"]!.AsObject()).Should().NotBeEmpty().And.OnlyContain(path => quotes.ContainsKey(path), "every value has a quote");
        var text = Squash(BookingIntakeSamples.Good);
        quotes.Select(quote => quote.Value!.GetValue<string>()).Should().OnlyContain(quote => text.Contains(Squash(quote), StringComparison.Ordinal), "every quote is in the text");
    }

    [Fact]
    public async Task Draft_ATextWithMissingValues_LeavesThemEmptyAndFlagged_NeverInvented()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Partial);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await BodyAsync(response);
        BookingDraftSchema.Errors(draft).Should().BeEmpty();
        var booking = draft["booking"]!.AsObject();
        var missing = draft["missing"]!.AsArray().ToDictionary(field => field!["field"]!.GetValue<string>(), field => field!["reason"]!.GetValue<string>());
        missing.Keys.Should().Contain(["bookingRef", "pickupStreet", "pickupFrom", "receiverName", "items.0.weight"]);
        missing.Values.Should().OnlyContain(reason => reason.Length > 0);
        booking.ContainsKey("bookingRef").Should().BeFalse();
        booking.ContainsKey("pickupFrom").Should().BeFalse();
        booking["items"]!.AsArray()[0]!.AsObject().ContainsKey("weight").Should().BeFalse("\"ongeveer een ton\" is no weight");
        ValuePaths(booking).Should().NotIntersectWith(missing.Keys, "a value is either given or flagged");
        booking["pickupCity"]!.GetValue<string>().Should().Be("Utrecht");
    }

    [Fact]
    public async Task Draft_AValueWithoutAQuoteInTheText_IsDroppedAndFlagged()
    {
        var invented = new JsonObject
        {
            ["booking"] = new JsonObject { ["pickupCity"] = "Utrecht", ["bookingRef"] = "SH-9999-0001" },
            ["quotes"] = new JsonObject { ["pickupCity"] = "Utrecht", ["bookingRef"] = "reference SH-9999-0001" },
            ["missing"] = new JsonArray(),
        };
        await using var host = new DraftApiHost(model: () => new ScriptedChatClient(_ => [new TextContent(invented.ToJsonString())]));

        using var response = await host.DraftAsync(BookingIntakeSamples.Partial);

        var draft = await BodyAsync(response);
        draft["booking"]!.AsObject().ContainsKey("bookingRef").Should().BeFalse("its quote is not in the text");
        draft["missing"]!.AsArray().Select(field => field!["field"]!.GetValue<string>()).Should().Contain("bookingRef");
    }

    [Fact]
    public async Task Draft_ATextWithTwoBookings_IsRefused422_AsMultipleBookingsInText()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.TwoBookings);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BodyAsync(response))["failureCode"]!.GetValue<string>().Should().Be("MultipleBookingsInText");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"booking": {"pickupCity": 5}, "quotes": {}, "missing": []}""")]
    public async Task Draft_AReplyOutsideTheSchema_IsAnsweredAsAgentUnavailable(string reply)
    {
        await using var host = new DraftApiHost(model: () => new ScriptedChatClient(_ => [new TextContent(reply)]));

        using var response = await host.DraftAsync(BookingIntakeSamples.Good);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await BodyAsync(response);
        body["failureCode"]!.GetValue<string>().Should().Be("AgentUnavailable");
        body.ContainsKey("booking").Should().BeFalse("nothing of the reply reaches the Shipper");
    }

    [Theory]
    [InlineData(BookingIntakeSamples.Good, "Mueller|Havenstraat|SH-2030-0142|Rotterdam|Anna|+49 221|850 kg")]
    [InlineData(BookingIntakeSamples.Partial, "Utrecht|Antwerpen|ongeveer een ton|magazijn")]
    [InlineData(BookingIntakeSamples.TwoBookings, "Breda|Gent|4 crates|Second booking")]
    public async Task Draft_NeitherTheTextNorTheDraftAppearsInTheTelemetry(string text, string secrets)
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(text);
        var answer = await response.Content.ReadAsStringAsync(Ct);

        var telemetry = host.Telemetry;
        telemetry.Should().Contain("flow=booking-draft", "there is a trail to search");
        foreach (var secret in secrets.Split('|'))
        {
            text.Should().Contain(secret, "the value is in the text, so its absence from the telemetry means something");
            telemetry.Should().NotContain(secret);
        }

        telemetry.Should().NotContain(text.Trim()).And.NotContain(answer);
    }

    [Fact]
    public async Task Draft_ADraftIsOnTheTrailWithTheTrackingKeys()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good);

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        host.Telemetry.Should().Contain($"correlationId={correlationId}").And.Contain("flow=booking-draft")
            .And.Contain($"partyId={DraftApiHost.ShipperId}").And.Contain("outcome=Succeeded");
    }

    [Theory]
    [InlineData("MultipleBookingsInText", "Refused")]
    [InlineData("AgentUnavailable", "Failed")]
    public async Task Draft_AnAnswerWithoutADraftIsOnTheTrailWithItsFailureCode(string failureCode, string outcome)
    {
        await using var host = failureCode == "AgentUnavailable"
            ? new DraftApiHost(model: () => new ScriptedChatClient(_ => [new TextContent("not json")]))
            : new DraftApiHost();

        using var response = await host.DraftAsync(failureCode == "AgentUnavailable" ? BookingIntakeSamples.Good : BookingIntakeSamples.TwoBookings);

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        host.Telemetry.Should().Contain($"correlationId={correlationId}").And.Contain($"outcome={outcome}").And.Contain($"failureCode={failureCode}");
    }

    [Fact]
    public async Task Draft_ARefusalAtTheEdgeIsOnTheTrailWithItsFailureCode()
    {
        await using var host = new DraftApiHost();

        using var response = await host.DraftAsync(BookingIntakeSamples.Good, token: host.PersonaToken(roles: WrongRole));

        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        host.Telemetry.Should().Contain($"correlationId={correlationId}").And.Contain("outcome=Refused").And.Contain("failureCode=NotAllowedToDraft");
    }

    [Fact]
    public async Task Draft_AFailedRun_LeavesNoReplyInTheTelemetry()
    {
        await using var host = new DraftApiHost(model: () => new ScriptedChatClient(_ => [new TextContent("""{"leak": "Mueller GmbH"}""")]));

        using var response = await host.DraftAsync(BookingIntakeSamples.Good);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Telemetry.Should().NotContain("Mueller").And.NotContain("\"leak\"").And.Contain("AgentUnavailable");
    }

    private static async Task<HttpStatusCode> DraftStatusAsync(DraftApiHost host)
    {
        using var response = await host.DraftAsync(BookingIntakeSamples.Good);
        return response.StatusCode;
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string saying)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status);
            (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(saying);
        }
    }

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();

    // The quote paths of a booking's values: "pickupCity", "items.0.count".
    private static List<string> ValuePaths(JsonObject booking) =>
    [
        .. booking.Where(property => property.Key != "items").Select(property => property.Key),
        .. booking["items"]?.AsArray().SelectMany((item, index) => item!.AsObject().Select(property => $"items.{index}.{property.Key}")) ?? [],
    ];

    private static string Squash(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
