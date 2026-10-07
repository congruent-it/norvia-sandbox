using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API answers a Shipper's tracking queries as API Management does (P8, ADR 0023): the same checks in
/// the same order as the four policies in infra/estate/partner-api/ (list, get, find and the download of a Proof of delivery
/// file), then the TMS Shipper tracking API as API Management's identity with the Shipper ID and the correlation ID, a short
/// wait and no retry, and the Liquid map of the answer. ShipperTrackingPolicyTests checks the same list against the policies.
/// </summary>
public sealed class ShipperTrackingTests : IAsyncLifetime
{
    private const string FileId = "7a0b1c5e-2f3a-4d6b-8c9d-0e1f2a3b4c5d";

    private readonly LocalPartnerApiFactory _factory = new();
    private HttpClient _partnerApi = null!;

    public static TheoryData<string> Routes { get; } = new()
    {
        "/partner/v1/transport-orders?page=1",
        "/partner/v1/transport-orders/find?shipperReference=BD-2030-0001",
        "/partner/v1/transport-orders/TO-0000001",
        $"/partner/v1/transport-orders/TO-0000001/proof-of-delivery/files/{FileId}",
    };

    public ValueTask InitializeAsync()
    {
        _partnerApi = _factory.CreateClient();
        _factory.Tms.Answer(HttpStatusCode.OK, "[]");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _partnerApi.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_WithoutASubscriptionKey_IsRefused401_AndTheTmsIsNotCalled(string route)
    {
        using var response = await GetAsync(route, subscriptionKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_WithAnInvalidToken_IsRefused401(string route)
    {
        using var response = await GetAsync(route, token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_AsAClientWithoutTheTrackingReadRole_IsRefused403_InThePolicysWords(string route)
    {
        // A Carrier Persona holds no Tracking.Read, and neither does a Shipper that may only book.
        using var carrier = await GetAsync(route, token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["Tracking.Submit"]));
        using var booker = await GetAsync(route, token: _factory.PersonaToken(roles: ["TransportOrders.Submit"]));

        foreach (var response in new[] { carrier, booker })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not read tracking.");
        }

        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_AsAClientThatIsNoShipper_IsRefused403_EvenWithTheRole(string route)
    {
        using var response = await GetAsync(route, token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["Tracking.Read"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Shipper is registered for this client.");
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_ChecksTheRoleBeforeTheShipper(string route)
    {
        using var response = await GetAsync(route, token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: ["TransportOrders.Submit"]));

        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not read tracking.");
    }

    [Fact]
    public async Task Query_CallsTheTmsAsApiManagementsIdentity_WithTheShipperFromTheToken_AndTheCorrelationId()
    {
        using var response = await GetAsync("/partner/v1/transport-orders/TO-0000001", extraHeaders: new() { ["X-Shipper-ID"] = "SHP-0003", ["X-Correlation-ID"] = "sent-by-the-caller" });

        var (call, _) = _factory.Tms.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Get);
        call.RequestUri!.PathAndQuery.Should().Be("/v1/shipper-tracking/transport-orders/TO-0000001");
        call.Headers.GetValues("X-Shipper-ID").Should().Equal("SHP-0001");
        call.Headers.GetValues("X-Correlation-ID").Should().Equal(CorrelationIdOf(response)).And.NotContain("sent-by-the-caller");
        call.Headers.Contains("Ocp-Apim-Subscription-Key").Should().BeFalse("the Shipper's key is for API Management");
        var token = new JsonWebToken(call.Headers.Authorization!.Parameter);
        token.GetClaim("azp").Value.Should().Be(LocalPartnerApiFactory.ApiManagementClientId);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("ShipperTracking.Read");
        token.Audiences.Should().Equal(LocalPartnerApiFactory.TmsAudience);
    }

    [Theory]
    [InlineData("/partner/v1/transport-orders?trackingStatus=InTransit&collectionFrom=2030-07-01&page=2", "/v1/shipper-tracking/transport-orders?trackingStatus=InTransit&collectionFrom=2030-07-01&page=2")]
    [InlineData("/partner/v1/transport-orders/find?shipperReference=BD%202030", "/v1/shipper-tracking/transport-orders/find?shipperReference=BD%202030")]
    [InlineData($"/partner/v1/transport-orders/TO-0000001/proof-of-delivery/files/{FileId}", $"/v1/shipper-tracking/transport-orders/TO-0000001/proof-of-delivery/files/{FileId}")]
    public async Task Query_AsksTheTmsForTheSameRoute_WithTheQueryStringAsItCame(string route, string tmsRoute)
    {
        _factory.Tms.Answer(HttpStatusCode.NotFound, "{}", "application/problem+json");

        using var response = await GetAsync(route);

        _factory.Tms.Calls.Single().Request.RequestUri!.PathAndQuery.Should().Be(tmsRoute);
    }

    [Theory]
    [InlineData("in-transit-summer")]
    [InlineData("delivered-winter")]
    [InlineData("clocks-back-london")]
    public async Task Get_AndFind_MapTheTmsAnswerToTheShipperTrackingFormat(string sample)
    {
        _factory.Tms.Answer(HttpStatusCode.OK, LocalPartnerApiFactory.ShipperTrackingSample($"{sample}.shipper-transport-order-detail.json"));
        var expected = JsonNode.Parse(LocalPartnerApiFactory.ShipperTrackingSample($"{sample}.shipper-tracking.json"));

        using var get = await GetAsync("/partner/v1/transport-orders/TO-0000001");
        using var find = await GetAsync("/partner/v1/transport-orders/find?shipperReference=BD-2030-0001");

        foreach (var response in new[] { get, find })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
            JsonNode.DeepEquals(JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), expected).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("mixed-statuses")]
    [InlineData("empty")]
    public async Task List_MapsTheTmsAnswerToTheShipperTrackingListFormat(string sample)
    {
        _factory.Tms.Answer(HttpStatusCode.OK, LocalPartnerApiFactory.ShipperTrackingListSample($"{sample}.shipper-transport-order-list-items.json"));
        var expected = JsonNode.Parse(LocalPartnerApiFactory.ShipperTrackingListSample($"{sample}.shipper-tracking-list.json"));

        using var response = await GetAsync("/partner/v1/transport-orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.DeepEquals(JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), expected).Should().BeTrue();
    }

    [Fact]
    public async Task Get_ATmsRefusal_PassesThroughUnchanged_WithTheCorrelationId()
    {
        const string problem = """{"title":"The Transport Order can't be tracked.","status":404,"code":"TransportOrderNotFound"}""";
        _factory.Tms.Answer(HttpStatusCode.NotFound, problem, "application/problem+json");

        using var response = await GetAsync("/partner/v1/transport-orders/TO-9999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be(problem);
        CorrelationIdOf(response).Should().NotBeNullOrEmpty();
        Keys("Refused").Should().Contain("failureCode", "TmsRefused");
    }

    [Fact]
    public async Task File_PassesThroughUnchanged_ItsBytes_ItsContentType_AndItsName()
    {
        byte[] pdf = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0xFE, 0x0A];
        _factory.Tms.AnswerFile(HttpStatusCode.OK, pdf, "application/pdf", "proof.pdf");

        using var response = await GetAsync($"/partner/v1/transport-orders/TO-0000001/proof-of-delivery/files/{FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("proof.pdf");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(pdf);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_WhenTheTmsFailsWithAServerError_AnswersTrackingIsTemporarilyUnavailable_WithoutRetrying(string route)
    {
        _factory.Tms.Answer(HttpStatusCode.InternalServerError, "boom");

        using var response = await GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await BodyAsync(response);
        body["text"]!.GetValue<string>().Should().Be("Tracking is temporarily unavailable. Try again shortly.");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        _factory.Tms.Calls.Should().ContainSingle("a failure reaches the Shipper at once");
        Keys("Failed").Should().Contain("failureCode", "TmsUnavailable");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Query_WhenTheTmsDoesNotAnswer_AnswersTheSameGatewayError_WithoutRetrying(string route)
    {
        _factory.Tms.Fail(new HttpRequestException("connection refused"));

        using var response = await GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("Tracking is temporarily unavailable. Try again shortly.");
        _factory.Tms.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Query_WhenTheTmsIsTooSlow_AnswersTheSameGatewayError()
    {
        _factory.Tms.Fail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing."));

        using var response = await GetAsync("/partner/v1/transport-orders");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/partner/v1/transport-orders/TO-0000001", """{"transportOrderNumber":"TO-0000001","trackingStatus":"Lost","collection":{"city":"A","countryCode":"NL"},"delivery":{"city":"B","countryCode":"DE"},"progress":[]}""")]
    [InlineData("/partner/v1/transport-orders", """[{"transportOrderNumber":"TO-0000001","trackingStatus":"Lost","collection":{"city":"A","countryCode":"NL"},"delivery":{"city":"B","countryCode":"DE"}}]""")]
    public async Task Query_WhenTheMapHasNoCodeForTheTrackingStatus_AnswersTheGatewayError_NotAWrongAnswer(string route, string tmsAnswer)
    {
        _factory.Tms.Answer(HttpStatusCode.OK, tmsAnswer);

        using var response = await GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Keys("Failed").Should().Contain("failureCode", "TrackingNotMapped");
    }

    [Fact]
    public async Task Query_LimitsTheCallsPerSubscription_To120APerMinute_AndRenewsTheLimit()
    {
        for (var call = 0; call < 120; call++)
        {
            using var allowed = await GetAsync("/partner/v1/transport-orders");
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var limited = await GetAsync("/partner/v1/transport-orders/TO-0000001");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the four operations share one counter");
        limited.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        Keys("Refused").Should().Contain("failureCode", "RateLimitExceeded");

        _factory.Time.Advance(TimeSpan.FromSeconds(61));
        using var renewed = await GetAsync("/partner/v1/transport-orders");
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Query_CountsTheLimitPerSubscription_NotForEveryone()
    {
        for (var call = 0; call < 121; call++)
        {
            using var response = await GetAsync("/partner/v1/transport-orders");
        }

        // Another subscription, with a token that passes the edge.
        using var otherSubscription = await GetAsync("/partner/v1/transport-orders", subscriptionKey: LocalPartnerApiFactory.VanDijkKey);

        otherSubscription.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Query_ARefusalAtTheEdge_IsNotCountedAgainstTheLimit()
    {
        for (var call = 0; call < 130; call++)
        {
            using var refused = await GetAsync("/partner/v1/transport-orders", token: _factory.PersonaToken(roles: ["TransportOrders.Submit"]));
        }

        using var response = await GetAsync("/partner/v1/transport-orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Query_EachQueryGetsItsOwnCorrelationId()
    {
        using var first = await GetAsync("/partner/v1/transport-orders");
        using var second = await GetAsync("/partner/v1/transport-orders");

        CorrelationIdOf(first).Should().NotBeNullOrEmpty().And.NotBe(CorrelationIdOf(second));
    }

    [Fact]
    public async Task Query_TracesItsHopWithTheTrackingKeys()
    {
        using var response = await GetAsync("/partner/v1/transport-orders");

        Keys("Succeeded").Should().Contain("flow", "shipper-tracking").And.Contain("partyId", "SHP-0001").And.Contain("correlationId", CorrelationIdOf(response));
    }

    [Fact]
    public async Task Query_ARefusal_IsTracedAsRefused()
    {
        using var response = await GetAsync("/partner/v1/transport-orders", token: _factory.PersonaToken(roles: ["TransportOrders.Submit"]));

        Keys("Refused").Should().Contain("failureCode", "NotAllowedToReadTracking").And.Contain("flow", "shipper-tracking");
    }

    private async Task<HttpResponseMessage> GetAsync(
        string route,
        string? subscriptionKey = LocalPartnerApiFactory.BrouwerKey,
        string token = "",
        Dictionary<string, string>? extraHeaders = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Length == 0 ? _factory.PersonaToken(roles: ["Tracking.Read"]) : token);
        foreach (var (name, value) in extraHeaders ?? [])
        {
            request.Headers.Add(name, value);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The keys in scope when the stand-in traced its outcome, as the dashboard receives them.
    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Where(log => log.Category == typeof(ShipperTrackingQueries).FullName)
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
