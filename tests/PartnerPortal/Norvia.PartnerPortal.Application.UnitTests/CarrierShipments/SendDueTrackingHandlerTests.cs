using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

/// <summary>
/// Auto-progress at the portal's boundary: the real send use case runs, and the recording fake stands in for API Management,
/// so what it recorded is what Norvia would have received.
/// </summary>
public sealed class SendDueTrackingHandlerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private static readonly ProofOfDeliveryUpload Cmr = new("sample-cmr.pdf", "application/pdf", "%PDF-1.4"u8.ToArray());

    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IReadRepository<CarrierBookingRequest> _readRepository = Substitute.For<IReadRepository<CarrierBookingRequest>>();
    private readonly RecordingPartnerApi _partnerApi = new();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ISampleCmr _sampleCmr = Substitute.For<ISampleCmr>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly CarrierBookingRequest _request;
    private readonly SendDueTrackingHandler _handler;

    public SendDueTrackingHandlerTests()
    {
        _personas.Find(Samples.KesslerSpedition.Id).Returns(Samples.KesslerSpedition);
        _request = ConfirmedRequest(Delay);
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(_request);
        _readRepository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(_request);
        _repository.ListAsync(Arg.Any<CarrierBookingRequestsDueForAutoProgressSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => _request.AutoProgressDueAt <= _time.GetUtcNow() ? [_request] : []);
        _repository.FirstOrDefaultAsync(Arg.Any<NextAutoProgressSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _request.AutoProgressDueAt is null ? null : _request);

        var send = new SendTrackingHandler(_repository, _partnerApi, _personas, _time, new FakeLogger<SendTrackingHandler>());
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<SendTrackingCommand>(), Arg.Any<CancellationToken>()).Returns(call => Forward(send, call.Arg<SendTrackingCommand>(), call.Arg<CancellationToken>()));
#pragma warning restore CA2012
        _sampleCmr.Read().Returns(Cmr);
        var sendProof = new SendProofOfDeliveryHandler(_readRepository, _partnerApi, _personas, _time);
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<SendProofOfDeliveryCommand>(), Arg.Any<CancellationToken>()).Returns(call => ForwardProof(sendProof, call.Arg<SendProofOfDeliveryCommand>(), call.Arg<CancellationToken>()));
#pragma warning restore CA2012
        _handler = new SendDueTrackingHandler(_repository, _mediator, _sampleCmr, _time, new FakeLogger<SendDueTrackingHandler>());
    }

    private static async ValueTask<Result<SentProofOfDelivery>> ForwardProof(SendProofOfDeliveryHandler send, SendProofOfDeliveryCommand command, CancellationToken cancellationToken) =>
        await send.Handle(command, cancellationToken);

    private static async ValueTask<Result<SentTrackingEvent>> Forward(SendTrackingHandler send, SendTrackingCommand command, CancellationToken cancellationToken) =>
        await send.Handle(command, cancellationToken);

    private static CarrierBookingRequest ConfirmedRequest(TimeSpan? delay)
    {
        var request = CarrierBookingRequest.Receive(
            Samples.KesslerSpedition.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now.AddHours(-1),
            new CarrierBookingDetails(
                new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
                new CarrierBookingStop("Oudegracht 112", "3511 AW", "Utrecht", "NLD", null, null), null, []));
        request.Answer(new CarrierAnswer.Confirmation("KES-77812"), "answer-1", Samples.Now);
        request.ScheduleAutoProgress(delay, Samples.Now);
        return request;
    }

    private ValueTask<AutoProgressOutcome> Sweep() => _handler.Handle(new SendDueTrackingCommand(), CancellationToken.None);

    private IEnumerable<TrackingEventType> Sent => _partnerApi.TrackingEvents.Select(sent => sent.Event.Type);

    [Fact]
    public async Task Handle_BeforeTheDelayHasPassed_SendsNothing_AndSaysWhenTheFirstEventIsDue()
    {
        var outcome = await Sweep();

        Sent.Should().BeEmpty();
        outcome.NextDueAt.Should().Be(Samples.Now + Delay);
    }

    [Fact]
    public async Task Handle_EachTimeADelayHasPassed_SendsTheNextOfCollectedDepartedOutForDeliveryAndDelivered_ThroughTheSamePathAsAButton()
    {
        for (var step = 0; step < 4; step++)
        {
            _time.Advance(Delay);
            await Sweep();
        }

        Sent.Should().Equal(TrackingEventType.Collected, TrackingEventType.Departed, TrackingEventType.OutForDelivery, TrackingEventType.Delivered);
        _partnerApi.TrackingEvents.Select(sent => sent.Persona).Should().AllBeEquivalentTo(Samples.KesslerSpedition);
        _partnerApi.TrackingEvents.Select(sent => sent.Event.EventId).Should().OnlyHaveUniqueItems();
        _request.Stage.Should().Be(ShipmentStage.Delivered);
    }

    private async Task DeliverAsync()
    {
        for (var step = 0; step < 4; step++)
        {
            _time.Advance(Delay);
            await Sweep();
        }
    }

    [Fact]
    public async Task Handle_ADelayAfterDelivered_SendsACleanProofOfDeliveryWithTheSampleCmr_ThroughTheSamePathAsAManualOne()
    {
        await DeliverAsync();
        var afterDelivered = await Sweep();
        afterDelivered.NextDueAt.Should().Be(_request.AutoProgressDueAt, "the Proof of delivery is the next thing due");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty("the delay hasn't passed");

        _time.Advance(Delay);
        var outcome = await Sweep();

        var (persona, shipmentNumber, proof) = _partnerApi.ProofsOfDelivery.Should().ContainSingle().Subject;
        persona.Should().Be(Samples.KesslerSpedition);
        shipmentNumber.Should().Be("SH-0000042");
        proof.Condition.Should().Be(ProofOfDeliveryCondition.Clean);
        proof.Remark.Should().BeNull();
        proof.Files.Should().ContainSingle().Which.Name.Should().Be("sample-cmr.pdf");
        proof.CarrierBookingNumber.Should().Be("CB-0000001");
        outcome.NextDueAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AfterTheProofOfDelivery_SendsNothingMore_AndHasNoNextDueTime()
    {
        await DeliverAsync();
        _time.Advance(Delay);
        await Sweep();

        _time.Advance(Delay);
        var outcome = await Sweep();

        _partnerApi.TrackingEvents.Should().HaveCount(4);
        _partnerApi.ProofsOfDelivery.Should().HaveCount(1);
        outcome.NextDueAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenNorviaDoesNotTakeTheProofOfDelivery_TriesAgainShortly_AndSendsItOnceItDoes()
    {
        await DeliverAsync();
        _partnerApi.Reply = TrackingReply.NotReceived(null);
        _time.Advance(Delay);

        var outcome = await Sweep();

        outcome.NextDueAt.Should().Be(_time.GetUtcNow() + SendDueTrackingHandler.RetryAfter);

        _partnerApi.Reply = TrackingReply.Accepted("corr-2");
        _time.Advance(SendDueTrackingHandler.RetryAfter);
        var retried = await Sweep();

        _partnerApi.ProofsOfDelivery.Should().HaveCount(2);
        retried.NextDueAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AShipmentDeliveredByAManualPersona_SendsNoProofOfDelivery()
    {
        var manual = ConfirmedRequest(delay: null);
        manual.ReportTracking(TrackingEventType.Delivered, Samples.Now);
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(manual);
        _readRepository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(manual);
        _repository.ListAsync(Arg.Any<CarrierBookingRequestsDueForAutoProgressSpec>(), Arg.Any<CancellationToken>()).Returns(_ => []);

        _time.Advance(TimeSpan.FromHours(1));
        await Sweep();

        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AfterAnEvent_WaitsAFullDelayBeforeTheNext()
    {
        _time.Advance(Delay);
        await Sweep();

        _time.Advance(Delay - TimeSpan.FromSeconds(1));
        var outcome = await Sweep();

        Sent.Should().Equal(TrackingEventType.Collected);
        outcome.NextDueAt.Should().Be(Samples.Now + Delay + Delay);
    }

    [Fact]
    public async Task Handle_AfterTheButtonSentAnEvent_ContinuesFromThere()
    {
        _request.ReportTracking(TrackingEventType.Collected, Samples.Now);
        _time.Advance(Delay);
        await Sweep();

        Sent.Should().Equal(TrackingEventType.Departed);
    }

    [Fact]
    public async Task Handle_AShipmentConfirmedByAManualPersona_SendsNothing_HoweverLongItWaits()
    {
        var manual = ConfirmedRequest(delay: null);
        _repository.ListAsync(Arg.Any<CarrierBookingRequestsDueForAutoProgressSpec>(), Arg.Any<CancellationToken>()).Returns(_ => []);
        _repository.FirstOrDefaultAsync(Arg.Any<NextAutoProgressSpec>(), Arg.Any<CancellationToken>()).Returns((CarrierBookingRequest?)null);

        _time.Advance(TimeSpan.FromHours(1));
        var outcome = await Sweep();

        manual.AutoProgressDueAt.Should().BeNull();
        Sent.Should().BeEmpty();
        outcome.NextDueAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenNorviaRefusesTheEvent_TriesAgainShortly_AndKeepsTheShipmentWhereItWas()
    {
        _partnerApi.Reply = TrackingReply.NotReceived(null);
        _time.Advance(Delay);

        var outcome = await Sweep();

        _request.Stage.Should().Be(ShipmentStage.Booked);
        _request.AutoProgressDueAt.Should().Be(_time.GetUtcNow() + SendDueTrackingHandler.RetryAfter);
        outcome.NextDueAt.Should().Be(_request.AutoProgressDueAt);
        await _repository.Received(1).UpdateAsync(_request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNorviaTakesTheEventOnTheSecondTry_SendsTheSameStepAgain()
    {
        _partnerApi.Reply = TrackingReply.NotReceived(null);
        _time.Advance(Delay);
        await Sweep();

        _partnerApi.Reply = TrackingReply.Accepted("corr-2");
        _time.Advance(SendDueTrackingHandler.RetryAfter);
        await Sweep();

        Sent.Should().Equal(TrackingEventType.Collected, TrackingEventType.Collected);
        _request.Stage.Should().Be(ShipmentStage.Collected);
    }
}
