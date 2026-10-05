using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API answers as API Management and the intake Logic App do together (ADR 0023): the same checks in
/// the same order as infra/estate/partner-api/submit-transport-order.policy.xml, then the Logic App's map and relay.
/// </summary>
public sealed class SubmitTransportOrderTests : IAsyncLifetime
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
    public async Task Submit_WithoutASubscriptionKey_IsRefused401_BeforeAnythingElse()
    {
        using var response = await SubmitAsync(subscriptionKey: null, token: "not-a-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        CorrelationIdOf(response).Should().NotBeNullOrEmpty();
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_WithAWrongSubscriptionKey_IsRefused401()
    {
        using var response = await SubmitAsync(subscriptionKey: "00000000000000000000000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_WithoutAToken_IsRefused401()
    {
        using var response = await SubmitAsync(token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        CorrelationIdOf(response).Should().NotBeNullOrEmpty();
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("another audience")]
    [InlineData("another key")]
    [InlineData("expired")]
    public async Task Submit_WithATokenNotValidForTheNorviaPartnerApi_IsRefused401(string wrong)
    {
        var token = wrong switch
        {
            "another audience" => _factory.PersonaToken(audience: LocalPartnerApiFactory.TmsAudience),
            "another key" => _factory.PersonaToken(signingKey: new byte[64]),
            _ => _factory.PersonaToken(expires: DateTime.UtcNow.AddHours(-1)),
        };

        using var response = await SubmitAsync(token: token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAClientWithoutTheSubmitRole_IsRefused403_InThePolicysWords()
    {
        using var response = await SubmitAsync(
            subscriptionKey: LocalPartnerApiFactory.VanDijkKey,
            token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: []));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyAsync(response);
        body["text"]!.GetValue<string>().Should().Be("This client may not submit Transport Orders.");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAClientThatIsNoShipper_IsRefused403()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Shipper is registered for this client.");
        _factory.Tms.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAShipper_SendsTheMappedTransportOrderToTheTms_ForTheShipperFromTheToken()
    {
        _factory.Tms.Answer(HttpStatusCode.Created, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted" }""");
        var booking = JsonNode.Parse(LocalPartnerApiFactory.Sample("minimal.booking.json"))!.AsObject();
        booking["shipperId"] = "SHP-0003";

        using var response = await SubmitAsync(booking: booking.ToJsonString());

        var (request, body) = _factory.Tms.Calls.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/v1/transport-orders");
        var expected = JsonNode.Parse(LocalPartnerApiFactory.Sample("minimal.transport-order.json"));
        JsonNode.DeepEquals(JsonNode.Parse(body), expected).Should().BeTrue(body);
        JsonNode.Parse(body)!["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
    }

    [Fact]
    public async Task Submit_AsAShipper_CallsTheTmsAsTheIntakeIdentity_UnderANewCorrelationId()
    {
        _factory.Tms.Answer(HttpStatusCode.Created, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted" }""");

        using var response = await SubmitAsync(correlationId: "sent-by-the-caller");

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
        token.GetClaim("azp").Value.Should().Be(LocalPartnerApiFactory.IntakeClientId);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal("TransportOrders.Intake");
    }

    [Fact]
    public async Task Submit_TheTmsAccepts_AnswersAccepted_WithTheTransportOrderNumber()
    {
        _factory.Tms.Answer(HttpStatusCode.Created, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted" }""");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await BodyAsync(response);
        body["result"]!.GetValue<string>().Should().Be("ACCEPTED");
        body["norviaOrderNo"]!.GetValue<string>().Should().Be("TO-0000042");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        body["bookingRef"]!.GetValue<string>().Should().Be("BD-2030-0001");
    }

    [Fact]
    public async Task Submit_TheTmsAnswersARepeat_AnswersAlreadyBooked()
    {
        _factory.Tms.Answer(HttpStatusCode.OK, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted", "repeat": true }""");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("ALREADY_BOOKED");
    }

    [Fact]
    public async Task Submit_TheTmsRejects_AnswersRejected_WithEveryReason()
    {
        _factory.Tms.Answer(
            HttpStatusCode.UnprocessableEntity,
            """{ "title": "Rejected", "reasons": [ { "code": "QuantityInvalid", "message": "Goods line 1 needs a quantity of at least 1." }, { "code": "WeightInvalid", "message": "Goods line 1 needs a weight greater than 0 kg." } ] }""",
            "application/problem+json");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await BodyAsync(response))["errors"]!.AsArray();
        errors.Select(error => error!["code"]!.GetValue<string>()).Should().Equal("QuantityInvalid", "WeightInvalid");
    }

    [Fact]
    public async Task Submit_TheTmsCantBeReached_AnswersNotReceived()
    {
        _factory.Tms.Fail(new HttpRequestException("Connection refused"));

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
    }

    [Fact]
    public async Task Submit_TheTmsFails_AnswersNotReceived_AfterRetrying()
    {
        _factory.Tms.Answer(HttpStatusCode.InternalServerError, "");

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        _factory.Tms.Calls.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task Submit_TheTmsAccepts_LogsTheTrackingKeys_AsTheIntakeRunTracksThem()
    {
        _factory.Tms.Answer(HttpStatusCode.Created, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted" }""");

        using var response = await SubmitAsync();

        TrackingKeysOfTheAnswer().Should().Contain("correlationId", CorrelationIdOf(response))
            .And.Contain("flow", "transport-order-intake")
            .And.Contain("partyId", "SHP-0001")
            .And.Contain("transportOrderNumber", "TO-0000042")
            .And.Contain("outcome", "Succeeded")
            .And.NotContainKey("failureCode");
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Refused", "TransportOrderRejected")]
    [InlineData(HttpStatusCode.BadRequest, "Failed", "TmsCallRefused")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Failed", "TmsUnavailable")]
    public async Task Submit_TheTmsDoesNotAccept_LogsTheOutcomeAndFailureCode_WithoutATransportOrderNumber(
        HttpStatusCode tmsStatus, string outcome, string failureCode)
    {
        _factory.Tms.Answer(tmsStatus, """{ "title": "Not taken in", "reasons": [ { "code": "QuantityInvalid", "message": "Goods line 1 needs a quantity of at least 1." } ] }""");

        using var response = await SubmitAsync();

        TrackingKeysOfTheAnswer().Should().Contain("outcome", outcome)
            .And.Contain("failureCode", failureCode)
            .And.NotContainKey("transportOrderNumber");
    }

    [Fact]
    public async Task Submit_RefusedAtTheEdge_LogsTheRefusal_AsTheAPIManagementPolicyTracesIt()
    {
        using var response = await SubmitAsync(
            subscriptionKey: LocalPartnerApiFactory.VanDijkKey,
            token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: []));

        TrackingKeysOfTheAnswer().Should().Contain("correlationId", CorrelationIdOf(response))
            .And.Contain("flow", "transport-order-intake")
            .And.Contain("outcome", "Refused")
            .And.Contain("failureCode", "NotAllowedToSubmit");
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        string? subscriptionKey = LocalPartnerApiFactory.BrouwerKey,
        string? token = "",
        string? booking = null,
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/transport-orders")
        {
            Content = new StringContent(booking ?? LocalPartnerApiFactory.Sample("minimal.booking.json"), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Length == 0 ? _factory.PersonaToken() : token);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The keys in scope when the stand-in logged its answer to the partner, as the dashboard receives them.
    private Dictionary<string, object?> TrackingKeysOfTheAnswer() =>
        _factory.Logs.GetSnapshot()
            .Last(log => log.Category == typeof(SubmitTransportOrder).FullName)
            .Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(keys => keys)
            .ToDictionary();

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
