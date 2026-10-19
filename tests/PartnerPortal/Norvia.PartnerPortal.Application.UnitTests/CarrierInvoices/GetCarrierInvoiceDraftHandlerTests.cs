using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierInvoices.Draft;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class GetCarrierInvoiceDraftHandlerTests
{
    private readonly IReadRepository<CarrierBookingRequest> _bookings = Substitute.For<IReadRepository<CarrierBookingRequest>>();
    private readonly IReadRepository<CarrierInvoice> _invoices = Substitute.For<IReadRepository<CarrierInvoice>>();
    private readonly IReadRepository<CarrierInvoiceOutcome> _outcomes = Substitute.For<IReadRepository<CarrierInvoiceOutcome>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly GetCarrierInvoiceDraftHandler _handler;

    public GetCarrierInvoiceDraftHandlerTests()
    {
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _bookings.ListAsync(Arg.Any<CarrierDeliveriesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new GetCarrierInvoiceDraftHandler(_bookings, _invoices, _outcomes, _personas, new FakeTimeProvider(Samples.Now));
    }

    private Task<CarrierInvoiceDraftView?> DraftAsync() =>
        _handler.Handle(new GetCarrierInvoiceDraftQuery(CarrierInvoiceSamples.Persona), CancellationToken.None).AsTask();

    [Fact]
    public async Task Handle_ListsTheDeliveredBookings_WithTheAgreedPrice_FromThePersonasOwnRecords()
    {
        _bookings.ListAsync(Arg.Any<CarrierDeliveriesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Booking("CB-0000001", "VDT-1", price: 120.50m), CarrierInvoiceSamples.Booking("CB-0000002", "VDT-2", price: null)]);

        var draft = await DraftAsync();

        draft!.Lines.Should().Equal(
            new CarrierInvoiceDraftLine("VDT-1", "CB-0000001", "SH-0000001", 120.50m),
            new CarrierInvoiceDraftLine("VDT-2", "CB-0000002", "SH-0000002", null));
    }

    [Fact]
    public async Task Handle_LeavesOutABookingAlreadyInvoiced_EvenWhileItWaitsForFinance()
    {
        _bookings.ListAsync(Arg.Any<CarrierDeliveriesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Booking("CB-0000001", "VDT-1"), CarrierInvoiceSamples.Booking("CB-0000002", "VDT-2")]);
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([CarrierInvoiceSamples.Invoice("INV-1", "VDT-1")]);

        var draft = await DraftAsync();

        draft!.Lines.Select(line => line.BookingRef).Should().Equal("VDT-2");
    }

    [Fact]
    public async Task Handle_OffersARejectedLineAgain()
    {
        _bookings.ListAsync(Arg.Any<CarrierDeliveriesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Booking("CB-0000001", "VDT-1"), CarrierInvoiceSamples.Booking("CB-0000002", "VDT-2")]);
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([CarrierInvoiceSamples.Invoice("INV-1", "VDT-1", "VDT-2")]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Rejected), ("VDT-2", CarrierInvoiceOutcomeResult.Accepted))]);

        var draft = await DraftAsync();

        draft!.Lines.Select(line => line.BookingRef).Should().Equal("VDT-1");
    }

    [Fact]
    public async Task Handle_SuggestsANumberThePersonaHasNotUsed_DatedToday()
    {
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Invoice("INV-20260924-001", "VDT-1")]);

        var draft = await DraftAsync();

        draft!.SuggestedInvoiceNumber.Should().Be("INV-20260924-002");
        draft.InvoiceDate.Should().Be(new DateOnly(2026, 9, 24));
    }

    [Fact]
    public async Task Handle_ForAShipperPersona_IsNull()
    {
        var draft = await _handler.Handle(new GetCarrierInvoiceDraftQuery(Samples.BrouwerDranken.Id), CancellationToken.None);

        draft.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForNoPersona_IsNull()
    {
        var draft = await _handler.Handle(new GetCarrierInvoiceDraftQuery(new PersonaId("nobody")), CancellationToken.None);

        draft.Should().BeNull();
    }
}
