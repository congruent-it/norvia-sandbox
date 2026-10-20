using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// The local Partner API takes a Carrier invoice as API Management does (ADR 0023): the same checks in the same order as
/// infra/estate/partner-api/submit-carrier-invoice.policy.xml, then the invoice handed to the Carrier invoice Logic App's stand-in, whose
/// answer goes back to the Carrier unchanged.
/// </summary>
public sealed class SubmitCarrierInvoiceTests : IAsyncLifetime
{
    private const string Received = """{ "result": "RECEIVED", "invoiceNo": "VDT-2026-0412", "status": "RECEIVED" }""";

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
        _factory.CarrierInvoiceLogicApp.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsAShipperPersona_IsRefused403_InThePolicysWords_AndTheLogicAppIsNotCalled()
    {
        // A Shipper's token never holds the Carrier invoice role.
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: LocalPartnerApiFactory.BrouwerClientId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyAsync(response);
        body["text"]!.GetValue<string>().Should().Be("This client may not send Carrier invoices.");
        body["messageId"]!.GetValue<string>().Should().Be(CorrelationIdOf(response));
        _factory.CarrierInvoiceLogicApp.Calls.Should().BeEmpty();
        TrackingKeysOfTheInvoice().Should().Contain("flow", "carrier-invoice")
            .And.Contain("outcome", "Refused")
            .And.Contain("failureCode", "NotAllowedToSubmit");
    }

    [Fact]
    public async Task Submit_AsAClientThatIsNoCarrier_IsRefused403()
    {
        using var response = await SubmitAsync(token: _factory.PersonaToken(clientId: "norvia-local-persona-stranger", roles: ["CarrierInvoices.Submit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(response))["text"]!.GetValue<string>().Should().Be("No Carrier is registered for this client.");
        _factory.CarrierInvoiceLogicApp.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AsACarrier_HandsTheInvoiceAsItIs_WithTheCarrierOfTheToken_AndANewCorrelationId_AsApiManagement()
    {
        _factory.CarrierInvoiceLogicApp.Answer(HttpStatusCode.Created, Received);
        var invoice = LocalPartnerApiFactory.CarrierInvoiceSample("two-lines.carrier-invoice.json");

        using var response = await SubmitAsync(invoice: invoice, correlationId: "sent-by-the-caller", carrierHeader: "CAR-0003");

        var (request, body) = _factory.CarrierInvoiceLogicApp.Calls.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().Be("/carrier-invoice");
        body.Should().Be(invoice);
        request.Headers.GetValues("X-Carrier-ID").Single().Should().Be("CAR-0001");
        var correlationId = request.Headers.GetValues("X-Correlation-ID").Single();
        correlationId.Should().NotBe("sent-by-the-caller").And.Be(CorrelationIdOf(response));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(request.Headers.Authorization!.Parameter, new TokenValidationParameters
        {
            ValidIssuer = LocalPartnerApiFactory.Issuer,
            ValidAudience = LocalPartnerApiFactory.CarrierInvoiceTriggerAudience,
            IssuerSigningKey = new SymmetricSecurityKey(_factory.SigningKey),
        });
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        ((JsonWebToken)validation.SecurityToken).GetClaim("azp").Value.Should().Be(LocalPartnerApiFactory.ApiManagementClientId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Submit_TheLogicAppAnswers_TheCarrierGetsItsStatusAndBody(HttpStatusCode status)
    {
        _factory.CarrierInvoiceLogicApp.Answer(status, Received);

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(status);
        (await BodyAsync(response))["invoiceNo"]!.GetValue<string>().Should().Be("VDT-2026-0412");
    }

    [Fact]
    public async Task Submit_TheLogicAppCantBeReached_AnswersNotReceived()
    {
        _factory.CarrierInvoiceLogicApp.Fail(new HttpRequestException("Connection refused"));

        using var response = await SubmitAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await BodyAsync(response))["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        TrackingKeysOfTheInvoice().Should().Contain("outcome", "Failed").And.Contain("failureCode", "InvoiceNotReceived");
    }

    [Fact]
    public async Task Submit_Received_LogsTheTrackingKeys_AsThePolicyTracesThem()
    {
        _factory.CarrierInvoiceLogicApp.Answer(HttpStatusCode.Created, Received);

        using var response = await SubmitAsync();

        TrackingKeysOfTheInvoice().Should().Contain("correlationId", CorrelationIdOf(response))
            .And.Contain("flow", "carrier-invoice")
            .And.Contain("partyId", "CAR-0001")
            .And.Contain("outcome", "Succeeded")
            .And.NotContainKey("failureCode");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Refused", "CarrierInvoiceUnreadable")]
    [InlineData(HttpStatusCode.Forbidden, "Refused", "CarrierNotAllowed")]
    [InlineData(HttpStatusCode.Conflict, "Refused", "InvoiceNumberUsed")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Refused", "CarrierNotFound")]
    [InlineData(HttpStatusCode.InternalServerError, "Failed", "InvoiceNotReceived")]
    public async Task Submit_TheLogicAppRefusesOrFails_LogsTheOutcomeAndFailureCode(HttpStatusCode status, string outcome, string failureCode)
    {
        _factory.CarrierInvoiceLogicApp.Answer(status, "{}");

        using var response = await SubmitAsync();

        TrackingKeysOfTheInvoice().Should().Contain("outcome", outcome).And.Contain("failureCode", failureCode);
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        string? subscriptionKey = LocalPartnerApiFactory.VanDijkKey,
        string? token = "",
        string? invoice = null,
        string? correlationId = null,
        string? carrierHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/carrier-invoices")
        {
            Content = new StringContent(invoice ?? LocalPartnerApiFactory.CarrierInvoiceSample("two-lines.carrier-invoice.json"), Encoding.UTF8, "application/json"),
        };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", token.Length == 0 ? _factory.PersonaToken(clientId: LocalPartnerApiFactory.VanDijkClientId, roles: ["CarrierInvoices.Submit"]) : token);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        // Nothing the caller sends names the Carrier.
        if (carrierHeader is not null)
        {
            request.Headers.Add("X-Carrier-ID", carrierHeader);
        }

        return await _partnerApi.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Dictionary<string, object?> TrackingKeysOfTheInvoice() =>
        _factory.Logs.GetSnapshot()
            .Last(log => log.Category == typeof(SubmitCarrierInvoice).FullName)
            .Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(keys => keys)
            .ToDictionary();

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.SingleOrDefault() : null;

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
}
