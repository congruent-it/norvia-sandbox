using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierBookings.Receive;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class ReceiveCarrierBookingHandlerTests
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
        new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null),
        "Getränke Schmitz GmbH",
        []);

    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveCarrierBookingHandler _handler;

    public ReceiveCarrierBookingHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _handler = new ReceiveCarrierBookingHandler(_repository, _personas, new FakeTimeProvider(Samples.Now));
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
}
