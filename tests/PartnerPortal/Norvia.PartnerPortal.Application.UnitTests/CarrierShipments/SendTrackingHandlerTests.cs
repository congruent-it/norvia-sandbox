using Ardalis.SharedKernel;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class SendTrackingHandlerTests
{
    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly RecordingPartnerApi _partnerApi = new();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly FakeLogger<SendTrackingHandler> _logger = new();
    private readonly CarrierBookingRequest _request;
    private readonly SendTrackingHandler _handler;

    public SendTrackingHandlerTests()
    {
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _request = ConfirmedRequest();
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(_request);
        _handler = new SendTrackingHandler(_repository, _partnerApi, _personas, _time, _logger);
    }

    private static CarrierBookingRequest ConfirmedRequest(CarrierBookingStop? collection = null)
    {
        var request = CarrierBookingRequest.Receive(
            Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now.AddHours(-1),
            new CarrierBookingDetails(
                collection ?? new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
                new CarrierBookingStop("Oudegracht 112", "3511 AW", "Utrecht", "NLD", null, null), null, []));
        request.Answer(new CarrierAnswer.Confirmation("VDT-77812"), "answer-1", Samples.Now.AddMinutes(-30));
        return request;
    }

    private ValueTask<Ardalis.Result.Result<SentTrackingEvent>> Send(TrackingEventType type, string persona = "van-dijk-transport") =>
        _handler.Handle(new SendTrackingCommand(new PersonaId(persona), "CB-0000001", type), CancellationToken.None);

    [Fact]
    public async Task Handle_SendsTheEventToNorviaAsTheCarrierPersona_WithItsBookingReferenceAndNumber()
    {
        var result = await Send(TrackingEventType.Collected);

        result.IsSuccess.Should().BeTrue();
        var (persona, shipmentNumber, sent) = _partnerApi.TrackingEvents.Should().ContainSingle().Subject;
        persona.Should().Be(Samples.VanDijkTransport);
        shipmentNumber.Should().Be("SH-0000042");
        sent.BookingReference.Should().Be("VDT-77812");
        sent.CarrierBookingNumber.Should().Be("CB-0000001");
        sent.Type.Should().Be(TrackingEventType.Collected);
        sent.EventTime.Should().Be(Samples.Now);
    }

    [Fact]
    public async Task Handle_EachEventHasAUniqueEventId()
    {
        await Send(TrackingEventType.Damaged);
        await Send(TrackingEventType.Damaged);

        _partnerApi.TrackingEvents.Select(sent => sent.Event.EventId).Should().OnlyHaveUniqueItems().And.NotContainNulls();
    }

    [Theory]
    [InlineData(TrackingEventType.Collected, "Rotterdam", "3087 BM")]
    [InlineData(TrackingEventType.CollectionFailed, "Rotterdam", "3087 BM")]
    [InlineData(TrackingEventType.Damaged, "Rotterdam", "3087 BM")]
    [InlineData(TrackingEventType.Delivered, "Utrecht", "3511 AW")]
    [InlineData(TrackingEventType.DeliveryFailed, "Utrecht", "3511 AW")]
    public async Task Handle_TheLocationIsOneOfTheShipmentsAddresses(TrackingEventType type, string place, string postcode)
    {
        await Send(type);

        var sent = _partnerApi.TrackingEvents.Should().ContainSingle().Subject.Event;
        (sent.Place, sent.Postcode, sent.Country).Should().Be((place, postcode, "NLD"));
    }

    [Fact]
    public async Task Handle_AnAcceptedProgressEvent_MovesTheShipmentOn_AndKeepsIt()
    {
        var result = await Send(TrackingEventType.Collected);

        result.Value.Shipment.Stage.Should().Be(ShipmentStage.Collected);
        result.Value.Shipment.NextProgress.Should().Equal(TrackingEventType.Departed, TrackingEventType.Delivered);
        result.Value.Reply.Should().Be(TrackingReply.Accepted("corr-1"));
        result.Value.Recorded.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(_request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnException_IsSent_WithoutMovingTheShipment()
    {
        var result = await Send(TrackingEventType.CollectionFailed);

        _partnerApi.TrackingEvents.Should().ContainSingle();
        result.Value.Shipment.Stage.Should().Be(ShipmentStage.Booked);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingResult.NotReceived)]
    [InlineData(TrackingResult.Refused)]
    public async Task Handle_AnEventNorviaDidNotTakeIn_LeavesTheShipmentWhereItWas(TrackingResult result)
    {
        _partnerApi.Reply = result == TrackingResult.Refused ? TrackingReply.Refused("No.", "corr-2") : TrackingReply.NotReceived(null);

        var sent = await Send(TrackingEventType.Collected);

        sent.Value.Shipment.Stage.Should().Be(ShipmentStage.Booked);
        sent.Value.Reply.Result.Should().Be(result);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenThePortalCantKeepAnAcceptedEvent_SaysSo_AndLogsItsKeys()
    {
        _repository.UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("The database is down."));

        var result = await Send(TrackingEventType.Collected);

        result.Value.Recorded.Should().BeFalse();
        result.Value.Reply.IsAccepted.Should().BeTrue();
        var record = _logger.LatestRecord;
        record.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Error);
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary()
            .Should().Contain("correlationId", "corr-1").And.Contain("flow", "carrier-tracking");
    }

    [Fact]
    public async Task Handle_AnEventThatIsNotValidNext_IsInvalid_AndSendsNothing()
    {
        await Send(TrackingEventType.Collected);
        _partnerApi.TrackingEvents.Clear();

        var result = await Send(TrackingEventType.CollectionFailed);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Invalid);
        _partnerApi.TrackingEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AShipmentWhoseAddressHasNoLocation_IsInvalid_AndSendsNothing()
    {
        var request = ConfirmedRequest(new CarrierBookingStop(null, null, "Rotterdam", null, null, null));
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(request);

        var result = await Send(TrackingEventType.Collected);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Invalid);
        _partnerApi.TrackingEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ARequestThatIsNotConfirmed_IsNotFound()
    {
        var open = CarrierBookingRequest.Receive(
            Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now,
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(open);

        (await Send(TrackingEventType.Collected)).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
        _partnerApi.TrackingEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoSuchRequest_IsNotFound()
    {
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns((CarrierBookingRequest?)null);

        (await Send(TrackingEventType.Collected)).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
    }

    [Theory]
    [InlineData("brouwer-dranken")]
    [InlineData("nobody")]
    public async Task Handle_NotACarrierPersona_IsNotFound(string persona)
    {
        (await Send(TrackingEventType.Collected, persona)).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
        _partnerApi.TrackingEvents.Should().BeEmpty();
    }
}
