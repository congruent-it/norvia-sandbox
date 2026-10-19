using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperInvoices;
using Norvia.PartnerPortal.Application.ShipperInvoices.List;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperInvoices;

public sealed class ShipperInboxEndpointTests
{
    private static readonly Persona Brouwer = new(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "SHP-0001", []);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly IOptions<ShipperEndpointOptions> _options =
        Options.Create(new ShipperEndpointOptions { ApiKeys = { ["SHP-0001"] = "shipper-secret", ["SHP-0099"] = "ghost-secret" } });

    public ShipperInboxEndpointTests() => _personas.All.Returns([Brouwer]);

    private static ShipperInvoiceView Invoice() => new(
        "INV-2030-000001", InvoiceDocumentType.Invoice, new DateTimeOffset(2030, 11, 1, 8, 0, 0, TimeSpan.Zero), new DateOnly(2030, 11, 1), null,
        [new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80m)], 80m)], 80m, 16.80m, 96.80m, "corr-1");

    private static ShipperInvoiceView CreditNote() => Invoice() with
    {
        DocumentNumber = "CN-2030-000001",
        DocumentType = InvoiceDocumentType.CreditNote,
        Credit = new CreditedLine("INV-2030-000001", "TO-0000001", "Rate Card error"),
        CorrelationId = "corr-2",
    };

    private Task<Results<Ok<IReadOnlyList<ShipperInboxDocument>>, UnauthorizedHttpResult, NotFound>> List(string shipperId = "SHP-0001", string? apiKey = "shipper-secret") =>
        ShipperInboxEndpoint.ListAsync(shipperId, apiKey, _mediator, _personas, _options, CancellationToken.None);

    [Fact]
    public async Task List_WithTheShippersKey_ListsTheInboxLatestFirstAsTheShipperReadsIt()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Is<ListShipperInvoicesQuery>(query => query.PersonaId == Brouwer.Id), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<ShipperInvoiceView>>([CreditNote(), Invoice()]));
#pragma warning restore CA2012

        var result = await List();

        var documents = result.Result.Should().BeOfType<Ok<IReadOnlyList<ShipperInboxDocument>>>().Subject.Value!;
        documents.Select(document => document.DocumentNumber).Should().Equal("CN-2030-000001", "INV-2030-000001");
        documents[0].DocumentType.Should().Be("CREDIT_NOTE");
        (documents[0].CreditedInvoiceNumber, documents[0].CreditedTransportOrderNumber, documents[0].Reason).Should().Be(("INV-2030-000001", "TO-0000001", "Rate Card error"));
        documents[0].CorrelationId.Should().Be("corr-2");
        documents[1].DocumentType.Should().Be("INVOICE");
        documents[1].CreditedInvoiceNumber.Should().BeNull();
        documents[1].TransportOrderNumbers.Should().Equal("TO-0000001");
        (documents[1].TotalWithoutVat, documents[1].Vat, documents[1].TotalWithVat).Should().Be((80m, 16.80m, 96.80m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("another-shippers-key")]
    public async Task List_WithoutTheShippersKey_IsUnauthorizedAndReadsNothing(string? apiKey)
    {
        var result = await List(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ListShipperInvoicesQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_AKeyedShipperWithoutAPersona_IsNotFound()
    {
        var result = await List("SHP-0099", "ghost-secret");

        result.Result.Should().BeOfType<NotFound>();
    }
}
