using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperInvoices.Receive;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperInvoices;

public sealed class ReceiveShipperInvoiceHandlerTests
{
    private static readonly ShipperInvoiceContent Content = new(
        InvoiceDocumentType.Invoice, "INV-2030-000001", new DateOnly(2030, 11, 1), null,
        [new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80.00m)], 80.00m)],
        80.00m, 16.80m, 96.80m);

    private readonly IRepository<ShipperInvoice> _repository = Substitute.For<IRepository<ShipperInvoice>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveShipperInvoiceHandler _handler;

    public ReceiveShipperInvoiceHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _handler = new ReceiveShipperInvoiceHandler(_repository, _personas, new FakeTimeProvider(Samples.Now));
    }

    private static ReceiveShipperInvoiceCommand Command(string partyId = "SHP-0001") => new(partyId, "corr-1", Content);

    [Fact]
    public async Task Handle_ANewDocument_PutsItInTheShippersInbox()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveShipperInvoiceResult("INV-2030-000001", Repeated: false));
        await _repository.Received(1).AddAsync(
            Arg.Is<ShipperInvoice>(invoice =>
                invoice.PersonaId == Samples.BrouwerDranken.Id
                && invoice.DocumentNumber == "INV-2030-000001"
                && invoice.CorrelationId == "corr-1"
                && invoice.ReceivedAt == Samples.Now
                && invoice.Content == Content),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameDocumentAgain_IsAcknowledged_WithoutBeingStoredTwice()
    {
        _repository.AnyAsync(Arg.Any<ShipperInvoiceByDocumentNumberSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReceiveShipperInvoiceResult("INV-2030-000001", Repeated: true));
        await _repository.DidNotReceive().AddAsync(Arg.Any<ShipperInvoice>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SHP-9999")]
    [InlineData("CAR-0001")]
    public async Task Handle_ForNoShipperPersona_IsNotFound(string partyId)
    {
        var result = await _handler.Handle(Command(partyId), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _repository.DidNotReceive().AddAsync(Arg.Any<ShipperInvoice>(), Arg.Any<CancellationToken>());
    }
}
