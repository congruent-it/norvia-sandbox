using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class AnswerCarrierBookingHandlerTests
{
    private static readonly CarrierAnswer Confirmation = new CarrierAnswer.Confirmation("VDT-77812", new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2)));
    private static readonly CarrierAnswer Rejection = new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity, "No trucks on Friday");

    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IRepository<CarrierTrackingSettings> _trackingSettings = Substitute.For<IRepository<CarrierTrackingSettings>>();
    private readonly IAutoProgressSignal _autoProgress = Substitute.For<IAutoProgressSignal>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly FakeLogger<AnswerCarrierBookingHandler> _logger = new();
    private readonly CarrierBookingRequest _request;
    private readonly AnswerCarrierBookingHandler _handler;

    public AnswerCarrierBookingHandlerTests()
    {
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _personas.Find(Samples.KesslerSpedition.Id).Returns(Samples.KesslerSpedition);
        _request = CarrierBookingRequest.Receive(
            Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now.AddHours(-1),
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(_request);
        _handler = new AnswerCarrierBookingHandler(_repository, _trackingSettings, _partnerApi, _personas, _autoProgress, _time, _logger);
    }

    private ValueTask<Result<AnsweredCarrierBooking>> Answer(CarrierAnswer answer, string persona = "van-dijk-transport") =>
        _handler.Handle(new AnswerCarrierBookingCommand(persona, "CB-0000001", answer), CancellationToken.None);

    private void NorviaReplies(NorviaReply reply) =>
        _partnerApi.AnswerAsync(Samples.VanDijkTransport, "CB-0000001", "SH-0000042", Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>()).Returns(reply);

    [Fact]
    public async Task Handle_ARecordedConfirmation_ConfirmsTheRequestInTheInbox_AndKeepsIt()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));

        var result = await Answer(Confirmation);

        result.IsSuccess.Should().BeTrue();
        result.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        result.Value.Request.CarrierBookingReference.Should().Be("VDT-77812");
        result.Value.Request.AnswerCorrelationId.Should().Be("answer-1");
        result.Value.Request.ClosedAt.Should().Be(Samples.Now);
        result.Value.Reply!.IsRecorded.Should().BeTrue();
        result.Value.Recorded.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(_request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARecordedRejection_RejectsTheRequestWithItsReason()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));

        var result = await Answer(Rejection);

        result.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.Rejected);
        result.Value.Request.RejectionReason.Should().Be(CarrierRejectionReason.NoCapacity);
        result.Value.Request.RejectionText.Should().Be("No trucks on Friday");
    }

    [Fact]
    public async Task Handle_SendsTheAnswerToNorviaAsTheCarrierPersona_ForTheRequestItAnswers()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));

        await Answer(Confirmation);

        await _partnerApi.Received(1).AnswerAsync(Samples.VanDijkTransport, "CB-0000001", "SH-0000042", Confirmation, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NorviaHasThisAnswerAlready_CloseTheRequestAsAnswered()
    {
        NorviaReplies(NorviaReply.Recorded("answer-0", repeated: true));

        var result = await Answer(Confirmation);

        result.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        result.Value.Reply!.Repeated.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_BookingNotOpen_ShowsTheRequestAsWithdrawnByNorvia()
    {
        NorviaReplies(NorviaReply.Refusal(CarrierAnswerResult.BookingNotOpen, "Withdrawn.", "answer-1"));

        var result = await Answer(Confirmation);

        result.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.WithdrawnByNorvia);
        result.Value.Request.CarrierBookingReference.Should().BeNull();
        result.Value.Request.AnswerCorrelationId.Should().Be("answer-1");
        result.Value.Reply!.Result.Should().Be(CarrierAnswerResult.BookingNotOpen);
        await _repository.Received(1).UpdateAsync(_request, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CarrierAnswerResult.NotReceived)]
    [InlineData(CarrierAnswerResult.Invalid)]
    [InlineData(CarrierAnswerResult.NotAllowed)]
    [InlineData(CarrierAnswerResult.BookingNotFound)]
    [InlineData(CarrierAnswerResult.Refused)]
    public async Task Handle_NorviaDoesNotRecordTheAnswer_LeavesTheRequestOpen_SoTheCarrierCanAnswerAgain(CarrierAnswerResult result)
    {
        NorviaReplies(result == CarrierAnswerResult.NotReceived ? NorviaReply.NotReceived(null) : NorviaReply.Refusal(result, "No.", "answer-1"));

        var answered = await Answer(Confirmation);

        answered.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.Open);
        answered.Value.Reply!.Result.Should().Be(result);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARequestThatIsNoLongerOpen_SendsNothing()
    {
        _request.Answer(Confirmation, "answer-0", Samples.Now);

        var result = await Answer(Rejection);

        result.Value.Reply.Should().BeNull();
        result.Value.Request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        await _partnerApi.DidNotReceive().AnswerAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NorviaRepliesButThePortalCantKeepIt_StillReturnsNorviasReply_AndLogsItOnTheMessageTrail()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));
        _repository.UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("database down"));

        var result = await Answer(Confirmation);

        result.Value.Recorded.Should().BeFalse();
        result.Value.Reply!.IsRecorded.Should().BeTrue();
        var log = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Error);
        var keys = log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary();
        keys.Should().Contain("flow", "carrier-answer").And.Contain("partyId", "CAR-0001")
            .And.Contain("carrierBookingNumber", "CB-0000001").And.Contain("shipmentNumber", "SH-0000042").And.Contain("correlationId", "answer-1");
    }

    [Fact]
    public async Task Handle_NorviaRepliesWithoutAMessageId_LogsNoCorrelationIdWhenItCantKeepIt()
    {
        NorviaReplies(NorviaReply.Recorded(messageId: null));
        _repository.UpdateAsync(Arg.Any<CarrierBookingRequest>(), Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("database down"));

        await Answer(Confirmation);

        _logger.Collector.GetSnapshot().Single().Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope)
            .ToDictionary().Should().NotContainKey("correlationId");
    }

    [Fact]
    public async Task Handle_APersonaThatIsNoCarrier_IsNotFound()
    {
        var result = await Answer(Confirmation, persona: "brouwer-dranken");

        result.Status.Should().Be(ResultStatus.NotFound);
        await _partnerApi.DidNotReceive().AnswerAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownPersona_IsNotFound()
    {
        (await Answer(Confirmation, persona: "nobody")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_ARequestThePersonaDoesntHave_IsNotFound()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns((CarrierBookingRequest?)null);

        (await Answer(Confirmation)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_AConfirmationByAPersonaInAutoProgress_SchedulesTheFirstTrackingEventADelayLater_AndWakesTheWorker()
    {
        var request = CarrierBookingRequest.Receive(
            Samples.KesslerSpedition.Id, "CB-0000002", "SH-0000043", "release-2", Samples.Now.AddHours(-1),
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(request);
        _partnerApi.AnswerAsync(Samples.KesslerSpedition, "CB-0000002", "SH-0000043", Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>()).Returns(NorviaReply.Recorded("answer-1"));

        await _handler.Handle(new AnswerCarrierBookingCommand("kessler-spedition", "CB-0000002", Confirmation), CancellationToken.None);

        request.AutoProgressDueAt.Should().Be(Samples.Now.AddSeconds(5));
        _autoProgress.Received(1).ShipmentConfirmed();
    }

    [Fact]
    public async Task Handle_AConfirmationUnderTheTrackingModeThePresenterChose_FollowsItOverTheSeedDefault()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));
        _trackingSettings.FirstOrDefaultAsync(Arg.Any<CarrierTrackingSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns(CarrierTrackingSettings.Choose(Samples.VanDijkTransport.Id, new CarrierTrackingPolicy(TrackingMode.AutoProgress, TimeSpan.FromSeconds(2))));

        await Answer(Confirmation);

        _request.AutoProgressDueAt.Should().Be(Samples.Now.AddSeconds(2));
    }

    [Fact]
    public async Task Handle_AConfirmationByAManualPersona_SchedulesNothing_AndWakesNoOne()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));

        await Answer(Confirmation);

        _request.AutoProgressDueAt.Should().BeNull();
        _autoProgress.DidNotReceive().ShipmentConfirmed();
    }

    [Fact]
    public async Task Handle_ARejection_SchedulesNothing()
    {
        NorviaReplies(NorviaReply.Recorded("answer-1"));
        _trackingSettings.FirstOrDefaultAsync(Arg.Any<CarrierTrackingSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns(CarrierTrackingSettings.Choose(Samples.VanDijkTransport.Id, new CarrierTrackingPolicy(TrackingMode.AutoProgress, TimeSpan.FromSeconds(2))));

        await Answer(Rejection);

        _request.AutoProgressDueAt.Should().BeNull();
        _autoProgress.DidNotReceive().ShipmentConfirmed();
    }
}
