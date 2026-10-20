using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcomes;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierInvoices;

public sealed class CarrierInboxEndpointsTests
{
    private static readonly Persona VanDijk = new(new PersonaId("van-dijk-transport"), "Van Dijk Transport", PartyType.Carrier, "CAR-0001", []);
    private static readonly DateTimeOffset At = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly IOptions<CarrierEndpointOptions> _options =
        Options.Create(new CarrierEndpointOptions { ApiKeys = { ["CAR-0001"] = "carrier-secret", ["CAR-0099"] = "ghost-secret" } });

    public CarrierInboxEndpointsTests() => _personas.All.Returns([VanDijk]);

    private static CarrierBookingRequestView Request(string number, decimal? price) => new(
        Guid.NewGuid(), number, $"SH-{number[3..]}", At,
        new CarrierBookingDetails(
            new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, [],
            price is { } total ? new CarrierCost("EUR", total, [new CarrierChargeLine("Freight", total)], IsSpot: false) : null),
        CarrierBookingRequestStatus.Confirmed, At, null, "VDT-SIM1-3");

    private Task<Results<Ok<IReadOnlyList<CarrierInboxBooking>>, UnauthorizedHttpResult, NotFound>> Bookings(string carrierId = "CAR-0001", string? apiKey = "carrier-secret") =>
        CarrierInboxEndpoints.ListBookingsAsync(carrierId, apiKey, _mediator, _personas, _options, CancellationToken.None);

    private Task<Results<Ok<IReadOnlyList<CarrierInboxOutcome>>, UnauthorizedHttpResult, NotFound>> Outcomes(string carrierId = "CAR-0001", string? apiKey = "carrier-secret") =>
        CarrierInboxEndpoints.ListOutcomesAsync(carrierId, apiKey, _mediator, _personas, _options, CancellationToken.None);

    [Fact]
    public async Task Bookings_WithTheCarriersKey_ListsTheInboxWithTheAgreedPrice()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Is<ListCarrierBookingRequestsQuery>(query => query.PersonaId == VanDijk.Id), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<CarrierBookingRequestView>>([Request("CB-0000002", 95.5m), Request("CB-0000001", null)]));
#pragma warning restore CA2012

        var result = await Bookings();

        var bookings = result.Result.Should().BeOfType<Ok<IReadOnlyList<CarrierInboxBooking>>>().Subject.Value!;
        bookings.Select(booking => (booking.CarrierBookingNumber, booking.AgreedAmount, booking.Status, booking.CarrierBookingReference)).Should().Equal(
            ("CB-0000002", 95.5m, "Confirmed", "VDT-SIM1-3"), ("CB-0000001", null, "Confirmed", "VDT-SIM1-3"));
    }

    [Fact]
    public async Task Outcomes_WithTheCarriersKey_ListsWhatNorviaToldTheLatestFirst_InTheOutcomeFormat()
    {
        var earlier = CarrierInvoiceOutcome.Receive(
            VanDijk.Id, "VDT-1", "corr-1", At, 100m, [new CarrierInvoiceOutcomeLine("TRX-1", CarrierInvoiceOutcomeResult.Matched, 100m, null)]);
        var later = CarrierInvoiceOutcome.Receive(
            VanDijk.Id, "VDT-2", "corr-2", At.AddHours(1), 110m,
            [new CarrierInvoiceOutcomeLine("TRX-2", CarrierInvoiceOutcomeResult.AcceptedAdjusted, 110m, "Agreed"), new CarrierInvoiceOutcomeLine("TRX-3", CarrierInvoiceOutcomeResult.Rejected, 0m, "No")]);
#pragma warning disable CA2012
        _mediator.Send(Arg.Is<ListCarrierInvoiceOutcomesQuery>(query => query.PersonaId == VanDijk.Id), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<CarrierInvoiceOutcome>>([earlier, later]));
#pragma warning restore CA2012

        var result = await Outcomes();

        var outcomes = result.Result.Should().BeOfType<Ok<IReadOnlyList<CarrierInboxOutcome>>>().Subject.Value!;
        outcomes.Select(outcome => outcome.InvoiceNo).Should().Equal("VDT-2", "VDT-1");
        outcomes[0].CorrelationId.Should().Be("corr-2");
        outcomes[0].ApprovedAmount.Should().Be(110m);
        outcomes[0].Lines.Select(line => (line.BookingRef, line.Result, line.AcceptedAmount, line.Remark)).Should().Equal(
            ("TRX-2", "ACCEPTED_ADJUSTED", 110m, "Agreed"), ("TRX-3", "REJECTED", 0m, "No"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("another-carriers-key")]
    public async Task WithoutTheCarriersKey_BothAreUnauthorized_AndReadNothing(string? apiKey)
    {
        (await Bookings(apiKey: apiKey)).Result.Should().BeOfType<UnauthorizedHttpResult>();
        (await Outcomes(apiKey: apiKey)).Result.Should().BeOfType<UnauthorizedHttpResult>();

        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ListCarrierBookingRequestsQuery>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ListCarrierInvoiceOutcomesQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKeyedCarrierWithoutAPersona_IsNotFound()
    {
        (await Bookings("CAR-0099", "ghost-secret")).Result.Should().BeOfType<NotFound>();
        (await Outcomes("CAR-0099", "ghost-secret")).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task AShippersPersonaIsNotACarrier()
    {
        _personas.All.Returns([new Persona(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "CAR-0001", [])]);

        (await Bookings()).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void WireNameOf_SpellsEveryResultAsTheOutcomeFormatDoes()
    {
        Enum.GetValues<CarrierInvoiceOutcomeResult>().Select(CarrierInvoiceOutcomeResults.WireNameOf).Should().Equal(CarrierInvoiceOutcomeResults.WireNames);
        FluentActions.Invoking(() => CarrierInvoiceOutcomeResults.WireNameOf((CarrierInvoiceOutcomeResult)99)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
