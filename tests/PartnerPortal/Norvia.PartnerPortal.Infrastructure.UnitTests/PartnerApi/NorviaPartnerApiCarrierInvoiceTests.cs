using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class NorviaPartnerApiCarrierInvoiceTests : IDisposable
{
    private const string Received = """
        { "result": "RECEIVED", "messageId": "corr-1", "invoiceNo": "VDT-1", "repeated": false, "status": "RECEIVED", "approvedAmount": null,
          "lines": [ { "bookingRef": "TRX-998", "amount": 70.50, "result": "MATCHED" }, { "bookingRef": "TRX-999", "amount": 99.00, "result": "PRICE_MISMATCH" } ] }
        """;

    private static readonly CarrierInvoiceLineDraft[] Lines = [new("TRX-998", 70.50m), new("TRX-999", 99.00m)];

    private readonly StubHandler _partnerApi = new();
    private readonly IPersonaTokens _tokens = Substitute.For<IPersonaTokens>();
    private readonly FakeLogger<NorviaPartnerApiClient> _logger = new();
    private readonly IPartnerPortalFaults _faults = Substitute.For<IPartnerPortalFaults>();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.CarrierInvoiceTests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaPartnerApiClient _client;

    public NorviaPartnerApiCarrierInvoiceTests()
    {
        _http = new HttpClient(_partnerApi) { BaseAddress = new Uri("https://apim.example/partner/v1/") };
        _tokens.GetTokenAsync(Samples.VanDijkTransport.Id, Arg.Any<CancellationToken>()).Returns("van-dijk-token");
        var options = Options.Create(new PartnerApiOptions
        {
            BaseAddress = _http.BaseAddress,
            PersonaClientIds = "van-dijk-transport=client-2",
            SubscriptionKeys = new() { ["van-dijk-transport"] = "van-dijk-key" },
        });
        _client = new NorviaPartnerApiClient(_http, _tokens, options, _activities, _faults, _logger);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _activities.Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
        _partnerApi.Dispose();
    }

    private Task<CarrierInvoiceReply> SubmitAsync() =>
        _client.SubmitCarrierInvoiceAsync(Samples.VanDijkTransport, "VDT-1", new DateOnly(2026, 10, 20), Lines, CancellationToken.None);

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_PostsItToCarrierInvoices_AsThePersona_InTheCarrierFormat_WithItsLength()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Received, "corr-1");

        await SubmitAsync();

        _partnerApi.Request!.Method.Should().Be(HttpMethod.Post);
        _partnerApi.Request.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/carrier-invoices"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("van-dijk-key");
        _partnerApi.Request.Headers.Authorization!.Parameter.Should().Be("van-dijk-token");
        _partnerApi.RequestContentLength.Should().BeGreaterThan(0);
        Contracts.CarrierInvoiceSchemaErrors(JsonNode.Parse(_partnerApi.RequestBody!)).Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_Received_CarriesEachLinesResult()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Received, "corr-1");

        var reply = await SubmitAsync();

        reply.Result.Should().Be(CarrierInvoiceReplyResult.Received);
        reply.MessageId.Should().Be("corr-1");
        reply.Receipt.Should().BeEquivalentTo(new CarrierInvoiceReceipt(
            Repeated: false, CarrierInvoiceStatus.Received, ApprovedAmount: null,
            [new("TRX-998", 70.50m, CarrierInvoiceLineResult.Matched), new("TRX-999", 99.00m, CarrierInvoiceLineResult.PriceMismatch)]));
    }

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_AnApprovedInvoice_CarriesTheApprovedAmount()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """
            { "result": "RECEIVED", "messageId": "corr-1", "invoiceNo": "VDT-1", "repeated": true, "status": "APPROVED", "approvedAmount": 70.50,
              "lines": [ { "bookingRef": "TRX-998", "amount": 70.50, "result": "MATCHED" } ] }
            """);

        var receipt = (await SubmitAsync()).Receipt!;

        receipt.Status.Should().Be(CarrierInvoiceStatus.Approved);
        receipt.ApprovedAmount.Should().Be(70.50m);
        receipt.Repeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{ "result": "INVOICE_NUMBER_USED", "messageId": "m", "text": "Used." }""", CarrierInvoiceReplyResult.InvoiceNumberUsed, "Used.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{ "result": "CARRIER_NOT_FOUND", "messageId": "m", "text": "No Carrier." }""", CarrierInvoiceReplyResult.CarrierNotFound, "No Carrier.")]
    [InlineData(HttpStatusCode.BadRequest, """{ "result": "INVALID", "messageId": "m", "text": "Bad date." }""", CarrierInvoiceReplyResult.Invalid, "Bad date.")]
    [InlineData(HttpStatusCode.Conflict, """{ "result": "INVOICE_NUMBER_USED", "messageId": "m" }""", CarrierInvoiceReplyResult.InvoiceNumberUsed, "Norvia has this invoice number with different content.")]
    [InlineData(HttpStatusCode.Forbidden, """{ "messageId": "m", "text": "Not a Carrier." }""", CarrierInvoiceReplyResult.Refused, "Not a Carrier.")]
    [InlineData(HttpStatusCode.Unauthorized, "", CarrierInvoiceReplyResult.Refused, PartnerAnswers.UnrecognisedCredentials)]
    public async Task SubmitCarrierInvoiceAsync_Refused_IsReadWithNorviasWords(HttpStatusCode status, string body, CarrierInvoiceReplyResult result, string text)
    {
        _partnerApi.Answer(status, body);

        var reply = await SubmitAsync();

        reply.Result.Should().Be(result);
        reply.Text.Should().Be(text);
        reply.Receipt.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, """{ "result": "NOT_RECEIVED", "messageId": "m", "text": "ERP down." }""")]
    [InlineData(HttpStatusCode.Created, """{ "result": "RECEIVED", "messageId": "m", "repeated": false, "status": "RECEIVED", "lines": [ { "bookingRef": "TRX-1", "amount": 1, "result": "SOMETHING_NEW" } ] }""")]
    [InlineData(HttpStatusCode.Created, """{ "result": "RECEIVED", "messageId": "m", "repeated": false, "status": "PAID", "lines": [ { "bookingRef": "TRX-1", "amount": 1, "result": "MATCHED" } ] }""")]
    [InlineData(HttpStatusCode.Created, """{ "result": "RECEIVED", "messageId": "m", "status": "RECEIVED", "lines": [ { "bookingRef": "TRX-1", "amount": 1, "result": "MATCHED" } ] }""")]
    [InlineData(HttpStatusCode.Created, """{ "result": "RECEIVED", "messageId": "m", "repeated": false, "status": "RECEIVED", "lines": [ { "amount": 1, "result": "MATCHED" } ] }""")]
    [InlineData(HttpStatusCode.Created, "not json")]
    public async Task SubmitCarrierInvoiceAsync_AnAnswerItCannotRelayInFull_IsNotReceived(HttpStatusCode status, string body)
    {
        _partnerApi.Answer(status, body);

        (await SubmitAsync()).Result.Should().Be(CarrierInvoiceReplyResult.NotReceived);
    }

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_NorviaUnreachable_IsNotReceived()
    {
        _partnerApi.Fail(new HttpRequestException("Connection refused"));

        (await SubmitAsync()).Should().Be(CarrierInvoiceReply.NotReceived(null));
    }

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_RunsInASpanOfItsOwn()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Received);

        await SubmitAsync();

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Send Carrier invoice");
    }

    [Fact]
    public async Task SubmitCarrierInvoiceAsync_LogsTheTrackingContractsKeys_NotTheAmounts()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Received, "corr-1");

        await SubmitAsync();

        var scope = _logger.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
        scope.Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "carrier-invoice",
            ["partyId"] = "CAR-0001",
            ["documentNumber"] = "VDT-1",
            ["correlationId"] = "corr-1",
            ["outcome"] = "Succeeded",
        });
        _logger.LatestRecord.Message.Should().NotContain("70");
    }
}
