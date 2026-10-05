using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API accepts a Carrier's tracking message as API Management does (ADR 0008, ADR 0023): the same
/// checks in the same order as infra/estate/partner-api/submit-carrier-tracking.policy.xml, then the message on the
/// tracking queue with the tracking contract's application properties and 202 Accepted. It never calls the TMS.
/// </summary>
public sealed class SubmitCarrierTrackingTests : IAsyncLifetime
{
    private readonly LocalPartnerApiFactory _factory = new();
    private HttpClient _partnerApi = null!;

    public ValueTask InitializeAsync()
    {
        _partnerApi = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _partnerApi.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Submit_WithoutASubscriptionKey_IsRefused401_AndNothingIsQueued()
    {
        using var response = await SubmitAsync(subscriptionKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.TrackingQueue.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_WithAnInvalidToken_IsRefused401()
    {
        using var response = await SubmitAsync(token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.TrackingQueue.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAClientWithoutTheTrackingSubmitRole_IsRefused403_InThePolicysWords()
    {
        // Answering bookings is not reporting tracking.
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["CarrierBookings.Respond"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not submit tracking.");
        _factory.TrackingQueue.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAClientThatIsNoCarrier_IsRefused403()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: ["Tracking.Submit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Carrier is registered for this client.");
        _factory.TrackingQueue.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsACarrier_QueuesTheMessageAsSent_AndAnswers202WithTheCorrelationId()
    {
        var tracking = LocalPartnerApiFactory.CarrierTrackingSample("collected.carrier-tracking.json");

        using var response = await SubmitAsync(body: tracking);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var message = _factory.TrackingQueue.Messages.Should().ContainSingle().Subject;
        message.Body.Should().Be(tracking);
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(message.Properties["correlationId"]).And.Be(CorrelationIdOf(response));
        message.MessageId.Should().Be(message.Properties["correlationId"]);
        _factory.Tms.Calls.Should().BeEmpty("the Logic App's work happens later, from the queue");
    }

    [Fact]
    public async Task Submit_SetsTheTrackingContractsApplicationProperties_TheCarrierFromTheToken_NotThePayload()
    {
        var tracking = JsonNode.Parse(LocalPartnerApiFactory.CarrierTrackingSample("collected.carrier-tracking.json"))!.AsObject();
        tracking["carrierId"] = "CAR-0003";

        using var response = await SubmitAsync(body: tracking.ToJsonString());

        _factory.TrackingQueue.Messages.Single().Properties.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["correlationId"] = CorrelationIdOf(response)!,
            ["partyId"] = "CAR-0001",
            ["sourceMessageId"] = tracking["eventId"]!.GetValue<string>(),
            ["flow"] = "carrier-tracking",
        });
    }

    [Fact]
    public async Task Submit_AMessageWithoutAnEventId_IsStillQueued_WithoutASourceMessageId()
    {
        using var response = await SubmitAsync(body: """{"statusCode":"PU"}""");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.TrackingQueue.Messages.Single().Properties.Keys.Should().BeEquivalentTo("correlationId", "partyId", "flow");
    }

    [Fact]
    public async Task Submit_AMessageThatIsNotJson_IsStillQueued_ForTheMapToFail()
    {
        using var response = await SubmitAsync(body: "not json");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.TrackingQueue.Messages.Single().Body.Should().Be("not json");
    }

    [Fact]
    public async Task Submit_EachMessageGetsItsOwnCorrelationId_WhateverTheCallerSent()
    {
        using var first = await SubmitAsync(correlationId: "sent-by-the-caller");
        using var second = await SubmitAsync();

        CorrelationIdOf(first).Should().NotBe("sent-by-the-caller").And.NotBe(CorrelationIdOf(second));
    }

    [Fact]
    public async Task Submit_WhenTheQueueCannotBeReached_Answers503_SoTheCarrierSendsItAgain()
    {
        _factory.TrackingQueue.Failure = new InvalidOperationException("Service Bus is down");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        Keys("Failed").Should().Contain("failureCode", "TrackingNotQueued");
    }

    [Fact]
    public async Task Submit_TracesItsHopWithTheTrackingKeys()
    {
        using var response = await SubmitAsync();

        var keys = Keys("Succeeded");
        keys.Should().Contain("flow", "carrier-tracking").And.Contain("partyId", "CAR-0001").And.Contain("correlationId", CorrelationIdOf(response));
    }

    [Fact]
    public async Task Submit_ARefusal_IsTracedAsRefused()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.BrouwerClientId));

        Keys("Refused").Should().Contain("failureCode", "NotAllowedToSubmitTracking");
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        string? subscriptionKey = LocalPartnerApiFactory.VanDijkKey,
        string token = "",
        string? body = null,
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/carrier-tracking")
        {
            Content = new StringContent(body ?? LocalPartnerApiFactory.CarrierTrackingSample("collected.carrier-tracking.json"), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token.Length == 0 ? _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["Tracking.Submit"]) : token);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The keys in scope when the stand-in traced its outcome, as the dashboard receives them.
    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Where(log => log.Category == typeof(SubmitCarrierTracking).FullName)
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
