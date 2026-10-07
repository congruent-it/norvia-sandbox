using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API answers a Carrier as API Management and the Carrier-answer Logic App do together (ADR 0023): the
/// same checks in the same order as infra/estate/partner-api/answer-carrier-booking.policy.xml, then the Logic App's map,
/// TMS call and relay.
/// </summary>
public sealed class AnswerCarrierBookingTests : IAsyncLifetime
{
    private const string Recorded = """{ "carrierBookingNumber": "CB-0000042", "status": "Confirmed", "shipmentNumber": "SH-0000007", "shipmentStatus": "Booked", "correlationId": "x", "repeat": false }""";

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
    public async Task Answer_WithoutASubscriptionKey_IsRefused401_BeforeAnythingElse()
    {
        using var response = await AnswerAsync(subscriptionKey: null, token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        CorrelationIdOf(response).Should().NotBeNullOrEmpty();
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_WithATokenNotValidForTheNorviaPartnerApi_IsRefused401()
    {
        using var response = await AnswerAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["CarrierBookings.Respond"], audience: LocalPartnerApiFactory.TmsAudience));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_AsAClientWithoutTheRespondRole_IsRefused403_InThePolicysWords()
    {
        // A Shipper's token holds the submit role only.
        using var response = await AnswerAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.BrouwerClientId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyAsync(response);
        body["text"]!.GetValue<string>().Should().Be("This client may not answer Carrier bookings.");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_AsAClientThatIsNoCarrier_IsRefused403()
    {
        using var response = await AnswerAsync(token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: ["CarrierBookings.Respond"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Carrier is registered for this client.");
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_AsACarrier_SendsTheMappedAnswerToTheTms_ForTheCarrierFromTheToken_NotThePayload()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, Recorded);
        var answer = JsonNode.Parse(LocalPartnerApiFactory.CarrierAnswerSample("confirmed.carrier-answer.json"))!.AsObject();
        answer["carrierId"] = "CAR-0003";

        using var response = await AnswerAsync(answer: answer.ToJsonString());

        var (request, body) = _factory.Tms.Calls.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/v1/carrier-bookings/CB-0000042/answer");
        JsonNode.DeepEquals(JsonNode.Parse(body), JsonNode.Parse(LocalPartnerApiFactory.CarrierAnswerSample("confirmed.carrier-booking-answer.json"))).Should().BeTrue(body);
        body.Should().NotContain("CAR-0003");
    }

    [Fact]
    public async Task Answer_AsACarrier_CallsTheTmsAsTheAnswerIdentity_UnderANewCorrelationId()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, Recorded);

        using var response = await AnswerAsync(correlationId: "sent-by-the-caller");

        var request = _factory.Tms.Calls.Single().Request;
        var correlationId = request.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBe("sent-by-the-caller").And.Be(CorrelationIdOf(response));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(request.Headers.Authorization!.Parameter, new TokenValidationParameters
        {
            ValidIssuer = LocalPartnerApiFactory.Issuer,
            ValidAudience = LocalPartnerApiFactory.TmsAudience,
            IssuerSigningKey = new SymmetricSecurityKey(_factory.SigningKey),
        });
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        var token = (JsonWebToken)validation.SecurityToken;
        token.GetClaim("azp").Value.Should().Be(LocalPartnerApiFactory.AnswerClientId);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("CarrierBookings.Answer");
    }

    [Fact]
    public async Task Answer_EachAnswerGetsItsOwnCorrelationId()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, Recorded);

        using var first = await AnswerAsync();
        using var second = await AnswerAsync();

        CorrelationIdOf(first).Should().NotBe(CorrelationIdOf(second));
    }

    [Fact]
    public async Task Answer_TheTmsRecordsIt_AnswersRecorded()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, Recorded);

        using var response = await AnswerAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body["result"]!.GetValue<string>().Should().Be("RECORDED");
        body["decision"]!.GetValue<string>().Should().Be("CONFIRMED");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
    }

    [Fact]
    public async Task Answer_TheTmsRefusesBecauseTheBookingIsNotOpen_RelaysBookingNotOpen()
    {
        _factory.Tms.Answer(HttpStatusCode.Conflict, """{ "title": "The Carrier's answer can't be recorded.", "code": "BookingNotOpen" }""", "application/problem+json");

        using var response = await AnswerAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("BOOKING_NOT_OPEN");
    }

    [Fact]
    public async Task Answer_TheTmsRefusesBecauseTheReferenceIsInUse_RelaysReferenceInUse()
    {
        _factory.Tms.Answer(HttpStatusCode.UnprocessableEntity, """{ "title": "The Carrier's answer can't be recorded.", "code": "ReferenceInUse" }""", "application/problem+json");

        using var response = await AnswerAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("REFERENCE_IN_USE");
    }

    [Fact]
    public async Task Answer_TheTmsCantBeReached_AnswersNotReceived()
    {
        _factory.Tms.Fail(new HttpRequestException("Connection refused"));

        using var response = await AnswerAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
    }

    [Fact]
    public async Task Answer_TheTmsFails_AnswersNotReceived_AfterRetrying()
    {
        _factory.Tms.Answer(HttpStatusCode.InternalServerError, "");

        using var response = await AnswerAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        _factory.Tms.Calls.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task Answer_ThatCantBeUsed_IsInvalid_AndNeverReachesTheTms()
    {
        using var response = await AnswerAsync(answer: """{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED" }""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await BodyAsync(response);
        body["result"]!.GetValue<string>().Should().Be("INVALID");
        body["text"]!.GetValue<string>().Should().Contain("bookingRef");
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_TheTmsRecordsIt_LogsTheTrackingKeys_AsTheAnswerRunTracksThem()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, Recorded);

        using var response = await AnswerAsync();

        TrackingKeysOfTheAnswer().Should().Contain("correlationId", CorrelationIdOf(response))
            .And.Contain("flow", "carrier-answer")
            .And.Contain("partyId", "CAR-0001")
            .And.Contain("carrierBookingNumber", "CB-0000042")
            .And.Contain("shipmentNumber", "SH-0000007")
            .And.Contain("outcome", "Succeeded")
            .And.NotContainKey("failureCode");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Refused", "BookingNotOpen")]
    [InlineData(HttpStatusCode.NotFound, "Refused", "BookingNotFound")]
    [InlineData(HttpStatusCode.Forbidden, "Refused", "CarrierNotAllowed")]
    [InlineData(HttpStatusCode.BadRequest, "Failed", "TmsCallRefused")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Failed", "TmsUnavailable")]
    public async Task Answer_TheTmsDoesNotRecordIt_LogsTheOutcomeAndFailureCode(HttpStatusCode tmsStatus, string outcome, string failureCode)
    {
        _factory.Tms.Answer(tmsStatus, "{}");

        using var response = await AnswerAsync();

        TrackingKeysOfTheAnswer().Should().Contain("outcome", outcome)
            .And.Contain("failureCode", failureCode)
            .And.Contain("carrierBookingNumber", "CB-0000042")
            .And.NotContainKey("shipmentNumber");
    }

    [Fact]
    public async Task Answer_ThatCantBeUsed_LogsItRefused()
    {
        using var response = await AnswerAsync(answer: """{ "decision": "MAYBE" }""");

        TrackingKeysOfTheAnswer().Should().Contain("outcome", "Refused")
            .And.Contain("failureCode", "CarrierAnswerUnreadable")
            .And.NotContainKey("carrierBookingNumber");
    }

    [Fact]
    public async Task Answer_RefusedAtTheEdge_LogsTheRefusal_AsTheAPIManagementPolicyTracesIt()
    {
        using var response = await AnswerAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.BrouwerClientId));

        TrackingKeysOfTheAnswer().Should().Contain("correlationId", CorrelationIdOf(response))
            .And.Contain("flow", "carrier-answer")
            .And.Contain("outcome", "Refused")
            .And.Contain("failureCode", "NotAllowedToRespond");
    }

    private async Task<HttpResponseMessage> AnswerAsync(
        string? subscriptionKey = LocalPartnerApiFactory.VanDijkKey,
        string? token = "",
        string? answer = null,
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/carrier-answers")
        {
            Content = new StringContent(answer ?? LocalPartnerApiFactory.CarrierAnswerSample("confirmed.carrier-answer.json"), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", token.Length == 0 ? _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["CarrierBookings.Respond"]) : token);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The keys in scope when the stand-in logged its answer to the Carrier, as the dashboard receives them.
    private Dictionary<string, object?> TrackingKeysOfTheAnswer() =>
        _factory.Logs.GetSnapshot()
            .Last(log => log.Category == typeof(AnswerCarrierBooking).FullName)
            .Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(keys => keys)
            .ToDictionary();

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
