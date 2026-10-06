using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API accepts a Carrier's Proof of delivery as API Management does (ADR 0009, ADR 0023): the same checks
/// in the same order as infra/estate/partner-api/submit-carrier-proof-of-delivery.policy.xml, then the body stored unparsed
/// in the inbound container, only then its reference on the Proof of delivery queue with the tracking contract's
/// application properties, and 202 Accepted. It never calls the TMS.
/// </summary>
public sealed class SubmitCarrierProofOfDeliveryTests : IAsyncLifetime
{
    private const string Sample = "clean-winter.carrier-proof-of-delivery.json";

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
    public async Task Submit_WithoutASubscriptionKey_IsRefused401_AndNothingIsStoredOrQueued()
    {
        using var response = await SubmitAsync(subscriptionKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        NothingKept();
    }

    [Fact]
    public async Task Submit_WithAnInvalidToken_IsRefused401()
    {
        using var response = await SubmitAsync(token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        NothingKept();
    }

    [Fact]
    public async Task Submit_AsAClientWithoutTheProofsOfDeliverySubmitRole_IsRefused403_InThePolicysWords()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["Tracking.Submit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("This client may not submit Proofs of delivery.");
        NothingKept();
    }

    [Fact]
    public async Task Submit_AsAClientThatIsNoCarrier_IsRefused403()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: ["ProofsOfDelivery.Submit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Carrier is registered for this client.");
        NothingKept();
    }

    [Fact]
    public async Task Submit_ABodyOver25Megabytes_IsRefused413_AndNothingIsStoredOrQueued()
    {
        using var response = await SubmitAsync(body: new string('x', (int)SubmitCarrierProofOfDelivery.MaxBodyBytes + 1));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        NothingKept();
        Keys("Refused").Should().Contain("failureCode", "ProofOfDeliveryTooLarge");
    }

    [Fact]
    public async Task Submit_ABodyOfExactly25Megabytes_IsAccepted()
    {
        using var response = await SubmitAsync(body: new string('x', (int)SubmitCarrierProofOfDelivery.MaxBodyBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Submit_AsACarrier_StoresTheBodyUnparsedAsABlobNamedForTheCorrelationId_AndAnswers202()
    {
        var proof = LocalPartnerApiFactory.CarrierProofOfDeliverySample(Sample);

        using var response = await SubmitAsync(body: proof);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var correlationId = CorrelationIdOf(response)!;
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(correlationId);
        Encoding.UTF8.GetString(_factory.InboundProofsOfDelivery.Blobs.Should().ContainSingle().Which.Value).Should().Be(proof, "the body is kept as received");
        _factory.InboundProofsOfDelivery.Blobs.Keys.Should().BeEquivalentTo(correlationId);
        _factory.Tms.Calls.Should().BeEmpty("the Logic App's work happens later, from the queue");
    }

    [Fact]
    public async Task Submit_QueuesOnlyTheBlobReference_AfterTheBlobIsStored()
    {
        using var response = await SubmitAsync();

        var message = _factory.ProofOfDeliveryQueue.Messages.Should().ContainSingle().Subject;
        var reference = JsonNode.Parse(message.Body)!.AsObject();
        reference.Select(property => property.Key).Should().BeEquivalentTo("container", "blobName");
        reference["container"]!.GetValue<string>().Should().Be("inbound-proofs-of-delivery");
        reference["blobName"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        message.MessageId.Should().Be(CorrelationIdOf(response));
        _factory.ProofOfDeliveryQueue.BlobWasStoredFirst.Should().Equal(true);
    }

    [Fact]
    public async Task Submit_SetsTheTrackingContractsApplicationProperties_TheCarrierFromTheToken_NotThePayload()
    {
        var proof = JsonNode.Parse(LocalPartnerApiFactory.CarrierProofOfDeliverySample(Sample))!.AsObject();
        proof["carrierId"] = "CAR-0003";

        using var response = await SubmitAsync(body: proof.ToJsonString());

        _factory.ProofOfDeliveryQueue.Messages.Single().Properties.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["correlationId"] = CorrelationIdOf(response)!,
            ["partyId"] = "CAR-0001",
            ["sourceMessageId"] = proof["documentId"]!.GetValue<string>(),
            ["flow"] = "carrier-proof-of-delivery",
        });
    }

    [Fact]
    public async Task Submit_AMessageWithoutADocumentId_IsStillAccepted_WithoutASourceMessageId()
    {
        using var response = await SubmitAsync(body: """{"bookingRef":"B1"}""");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.ProofOfDeliveryQueue.Messages.Single().Properties.Keys.Should().BeEquivalentTo("correlationId", "partyId", "flow");
    }

    [Fact]
    public async Task Submit_ABodyThatIsNotJson_IsStillStoredAndQueued_ForTheMapToFail()
    {
        using var response = await SubmitAsync(body: "not json");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Encoding.UTF8.GetString(_factory.InboundProofsOfDelivery.Blobs.Single().Value).Should().Be("not json");
    }

    [Fact]
    public async Task Submit_EachProofGetsItsOwnCorrelationId_WhateverTheCallerSent()
    {
        using var first = await SubmitAsync(correlationId: "sent-by-the-caller");
        using var second = await SubmitAsync();

        CorrelationIdOf(first).Should().NotBe("sent-by-the-caller").And.NotBe(CorrelationIdOf(second));
    }

    [Fact]
    public async Task Submit_WhenTheBlobCannotBeStored_Answers503_AndQueuesNothing()
    {
        _factory.InboundProofsOfDelivery.Failure = new InvalidOperationException("Storage is down");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _factory.ProofOfDeliveryQueue.Messages.Should().BeEmpty("a reference to a blob that isn't there is never queued");
        Keys("Failed").Should().Contain("failureCode", "ProofOfDeliveryNotStored");
    }

    [Fact]
    public async Task Submit_WhenTheReferenceCannotBeQueued_Answers503_SoTheCarrierSendsItAgain()
    {
        _factory.ProofOfDeliveryQueue.Failure = new InvalidOperationException("Service Bus is down");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        Keys("Failed").Should().Contain("failureCode", "ProofOfDeliveryNotQueued");
    }

    [Fact]
    public async Task Submit_TracesItsHopWithTheTrackingKeys()
    {
        using var response = await SubmitAsync();

        Keys("Succeeded").Should().Contain("flow", "carrier-proof-of-delivery").And.Contain("partyId", "CAR-0001").And.Contain("correlationId", CorrelationIdOf(response));
    }

    [Fact]
    public async Task Submit_ARefusal_IsTracedAsRefused()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.BrouwerClientId));

        Keys("Refused").Should().Contain("failureCode", "NotAllowedToSubmitProofOfDelivery");
    }

    private void NothingKept()
    {
        _factory.InboundProofsOfDelivery.Blobs.Should().BeEmpty();
        _factory.ProofOfDeliveryQueue.Messages.Should().BeEmpty();
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        string? subscriptionKey = LocalPartnerApiFactory.VanDijkKey,
        string token = "",
        string? body = null,
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/carrier-proofs-of-delivery")
        {
            Content = new StringContent(body ?? LocalPartnerApiFactory.CarrierProofOfDeliverySample(Sample), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", token.Length == 0 ? _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["ProofsOfDelivery.Submit"]) : token);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The keys in scope when the stand-in traced its outcome, as the dashboard receives them.
    private Dictionary<string, object?> Keys(string outcome) =>
        _factory.Logs.GetSnapshot()
            .Where(log => log.Category == typeof(SubmitCarrierProofOfDelivery).FullName)
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
