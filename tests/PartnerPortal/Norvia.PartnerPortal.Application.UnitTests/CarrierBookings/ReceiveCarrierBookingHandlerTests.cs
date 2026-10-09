using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;
using Norvia.PartnerPortal.Application.CarrierBookings.Receive;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class ReceiveCarrierBookingHandlerTests
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
        new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null),
        "Getränke Schmitz GmbH",
        []);

    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IRepository<CarrierAnswerSettings> _settings = Substitute.For<IRepository<CarrierAnswerSettings>>();
    private readonly IAutoAnswerSignal _signal = Substitute.For<IAutoAnswerSignal>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveCarrierBookingHandler _handler;

    public ReceiveCarrierBookingHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport, Samples.KesslerSpedition]);
        _handler = new ReceiveCarrierBookingHandler(_repository, _settings, _personas, _signal, new FakeTimeProvider(Samples.Now));
    }

    private static ReceiveCarrierBookingCommand Command(string partyId = "CAR-0001") =>
        new(partyId, "CB-0000001", "SH-0000042", "release-1", Details);

    [Fact]
    public async Task Handle_ANewRequest_PutsItInTheCarriersInbox()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false));
        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierBookingRequest>(request =>
                request.PersonaId == Samples.VanDijkTransport.Id
                && request.CarrierBookingNumber == "CB-0000001"
                && request.ShipmentNumber == "SH-0000042"
                && request.CorrelationId == "release-1"
                && request.ReceivedAt == Samples.Now
                && request.Details == Details),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameRequestAgain_IsIgnored_WithoutAnError()
    {
        _repository.AnyAsync(Arg.Any<CarrierBookingRequestByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeated.Should().BeTrue();
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CAR-9999")]
    [InlineData("SHP-0001")]
    public async Task Handle_ForNoCarrierPersona_IsNotFound(string partyId)
    {
        var result = await _handler.Handle(Command(partyId), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAManualCarrier_SchedulesNoAnswer_AndSignalsNothing()
    {
        await _handler.Handle(Command(), CancellationToken.None);

        await _repository.Received(1).AddAsync(Arg.Is<CarrierBookingRequest>(request => request.AutoAnswerMode == null && request.AutoAnswerDueAt == null), Arg.Any<CancellationToken>());
        _signal.DidNotReceive().RequestArrived();
    }

    [Fact]
    public async Task Handle_ForACarrierThatConfirmsByDefault_SchedulesTheAnswerAfterTheDelay_AndSignals()
    {
        await _handler.Handle(Command("CAR-0002"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierBookingRequest>(request => request.AutoAnswerMode == CarrierAnswerMode.AutoConfirm && request.AutoAnswerDueAt == Samples.Now.AddSeconds(5)),
            Arg.Any<CancellationToken>());
        _signal.Received(1).RequestArrived();
    }

    [Fact]
    public async Task Handle_WhenThePresenterChoseAnotherPolicy_FollowsItInsteadOfTheDefault()
    {
        _settings.FirstOrDefaultAsync(Arg.Any<CarrierAnswerSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns(
            CarrierAnswerSettings.Choose(Samples.KesslerSpedition.Id, new CarrierAnswerPolicy(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, TimeSpan.FromSeconds(2))));

        await _handler.Handle(Command("CAR-0002"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierBookingRequest>(request =>
                request.AutoAnswerMode == CarrierAnswerMode.AutoReject
                && request.AutoAnswerReason == CarrierRejectionReason.LaneNotServed
                && request.AutoAnswerDueAt == Samples.Now.AddSeconds(2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameRequestAgain_DoesNotSignal()
    {
        _repository.AnyAsync(Arg.Any<CarrierBookingRequestByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(Command("CAR-0002"), CancellationToken.None);

        _signal.DidNotReceive().RequestArrived();
    }
}
