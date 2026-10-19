using System.Text.Json;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.ShipperInvoices.Receive;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperInvoices;

public sealed class ShipperInvoiceEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOptions<ShipperEndpointOptions> _options =
        Options.Create(new ShipperEndpointOptions { ApiKeys = { ["SHP-0001"] = "shipper-secret" } });

    private static ShipperInvoiceBody Invoice() => new(
        "INVOICE", "INV-2030-000001", "01-11-2030", null, null, null,
        [new ShipperInvoiceLineBody("TO-0000001", "BD-2030-0001", "18-10-2030", [new ShipperChargeLineBody("FREIGHT", 80.00m)], 80.00m)],
        "EUR", 80.00m, 16.80m, 96.80m);

    private static ShipperInvoiceBody CreditNote() => Invoice() with
    {
        DocumentType = "CREDIT_NOTE",
        DocumentNumber = "CN-2030-000001",
        CreditedInvoiceNumber = "INV-2030-000001",
        CreditedTransportOrderNumber = "TO-0000001",
        Reason = "Rate Card error",
    };

    private static ShipperInvoiceLineBody Line(ShipperInvoiceBody body) => body.Lines![0]!;

    private Task<Results<Ok<ReceiveShipperInvoiceResult>, ProblemHttpResult, UnauthorizedHttpResult>> Receive(
        string shipperId = "SHP-0001", string? apiKey = "shipper-secret", string? correlationId = "corr-1", ShipperInvoiceBody? body = null) =>
        ShipperInvoiceEndpoint.ReceiveAsync(
            shipperId, apiKey, correlationId, body ?? Invoice(), new PassThroughRetrier(_mediator), _options, NullLoggerFactory.Instance, CancellationToken.None);

    private void Answers(bool repeated, string number = "INV-2030-000001") =>
        _mediator.Send(Arg.Any<ReceiveShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveShipperInvoiceResult(number, repeated)));

    // The Logic App addresses the endpoint by this shape.
    [Fact]
    public void PathOf_IsWhereTheLogicAppSendsAShippersInvoices() =>
        ShipperInvoiceEndpoint.PathOf("SHP-0001").Should().Be("/shipper/v1/SHP-0001/invoices");

    [Fact]
    public async Task Receive_AValidInvoice_IsPutInTheInboxAndAcknowledged()
    {
        Answers(repeated: false);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveShipperInvoiceResult>>().Which.Value!.Repeated.Should().BeFalse();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveShipperInvoiceCommand>(command =>
                command.PartyId == "SHP-0001"
                && command.CorrelationId == "corr-1"
                && command.Content.DocumentType == InvoiceDocumentType.Invoice
                && command.Content.DocumentNumber == "INV-2030-000001"
                && command.Content.DocumentDate == new DateOnly(2030, 11, 1)
                && command.Content.Credit == null
                && command.Content.Lines.Count == 1
                && command.Content.Lines[0].TransportOrderNumber == "TO-0000001"
                && command.Content.Lines[0].BookingRef == "BD-2030-0001"
                && command.Content.Lines[0].DeliveryDate == new DateOnly(2030, 10, 18)
                && command.Content.Lines[0].ChargeLines.Single() == new ShipperChargeLine(ChargeCode.Freight, 80.00m)
                && command.Content.Lines[0].LineTotal == 80.00m
                && command.Content.TotalWithoutVat == 80.00m
                && command.Content.Vat == 16.80m
                && command.Content.TotalWithVat == 96.80m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ACreditNote_IsTakenWithWhatItCredits()
    {
        Answers(repeated: false, "CN-2030-000001");

        var result = await Receive(body: CreditNote());

        result.Result.Should().BeOfType<Ok<ReceiveShipperInvoiceResult>>();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveShipperInvoiceCommand>(command =>
                command.Content.DocumentType == InvoiceDocumentType.CreditNote
                && command.Content.Credit == new CreditedLine("INV-2030-000001", "TO-0000001", "Rate Card error")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ARepeatedDocument_IsAcknowledgedToo_FlaggedAsRepeated()
    {
        Answers(repeated: true);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveShipperInvoiceResult>>().Which.Value!.Repeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Receive_AMissingOrWrongCredential_IsUnauthorized_AndStoresNothing(string? apiKey)
    {
        var result = await Receive(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveShipperInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForAShipperWithoutACredential_IsUnauthorized()
    {
        var result = await Receive(shipperId: "SHP-0003");

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    public static TheoryData<string?, ShipperInvoiceBody> InvalidRequests()
    {
        var valid = Invoice();
        var credit = CreditNote();
        var line = Line(valid);
        return new()
        {
            { null, valid },
            { "corr-1", valid with { DocumentType = null } },
            { "corr-1", valid with { DocumentType = "REFUND" } },
            { "corr-1", valid with { DocumentNumber = null } },
            { "corr-1", valid with { DocumentNumber = "INV-30-1" } },
            { "corr-1", valid with { DocumentNumber = "CN-2030-000001" } },
            { "corr-1", valid with { DocumentDate = "2030-11-01" } },
            { "corr-1", valid with { DocumentDate = "31-02-2030" } },
            { "corr-1", valid with { Currency = "USD" } },
            { "corr-1", valid with { Currency = null } },
            { "corr-1", valid with { TotalWithoutVat = null } },
            { "corr-1", valid with { Vat = -1m } },
            { "corr-1", valid with { TotalWithVat = 96.805m } },
            { "corr-1", valid with { Lines = null } },
            { "corr-1", valid with { Lines = [] } },
            { "corr-1", valid with { Lines = [null] } },
            { "corr-1", valid with { Lines = [line with { TransportOrderNumber = "SH-0000001" }] } },
            { "corr-1", valid with { Lines = [line with { BookingRef = " " }] } },
            { "corr-1", valid with { Lines = [line with { DeliveryDate = "later" }] } },
            { "corr-1", valid with { Lines = [line with { LineTotal = null }] } },
            { "corr-1", valid with { Lines = [line with { ChargeLines = null }] } },
            { "corr-1", valid with { Lines = [line with { ChargeLines = [] }] } },
            { "corr-1", valid with { Lines = [line with { ChargeLines = [null] }] } },
            { "corr-1", valid with { Lines = [line with { ChargeLines = [new ShipperChargeLineBody("FUEL", 1m)] }] } },
            { "corr-1", valid with { Lines = [line with { ChargeLines = [new ShipperChargeLineBody("FREIGHT", null)] }] } },
            { "corr-1", valid with { Reason = "Because" } },
            { "corr-1", valid with { CreditedInvoiceNumber = "INV-2030-000001" } },
            { "corr-1", credit with { CreditedInvoiceNumber = null } },
            { "corr-1", credit with { CreditedInvoiceNumber = "CN-2030-000001" } },
            { "corr-1", credit with { CreditedTransportOrderNumber = null } },
            { "corr-1", credit with { CreditedTransportOrderNumber = "TO-1" } },
            { "corr-1", credit with { Reason = " " } },
            { "corr-1", credit with { DocumentNumber = "INV-2030-000001" } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Receive_AnInvalidDocument_IsABadRequest_AndStoresNothing(string? correlationId, ShipperInvoiceBody body)
    {
        var result = await Receive(correlationId: correlationId, body: body);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveShipperInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForAShipperTheDirectoryDoesNotKnow_IsNotFound()
    {
        _mediator.Send(Arg.Any<ReceiveShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReceiveShipperInvoiceResult>.NotFound());

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    public static TheoryData<string> Samples()
    {
        var names = typeof(ShipperInvoiceEndpointTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("shipper-invoices/", StringComparison.Ordinal));
        var data = new TheoryData<string>();
        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    // The sample documents of the format are what the endpoint takes: the field names of the format bind to the body.
    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Receive_ASampleOfTheShipperInvoiceFormat_IsTaken(string sample)
    {
        Answers(repeated: false);
        using var stream = typeof(ShipperInvoiceEndpointTests).Assembly.GetManifestResourceStream(sample)!;
        var body = await JsonSerializer.DeserializeAsync<ShipperInvoiceBody>(stream, WebJson, Xunit.TestContext.Current.CancellationToken);

        var result = await Receive(body: body);

        result.Result.Should().BeOfType<Ok<ReceiveShipperInvoiceResult>>();
    }

    [Fact]
    public void TheSamples_AreFound() => Samples().Should().HaveCountGreaterThanOrEqualTo(2);
}
