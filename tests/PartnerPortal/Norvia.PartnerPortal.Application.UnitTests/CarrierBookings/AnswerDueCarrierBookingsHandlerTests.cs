using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class AnswerDueCarrierBookingsHandlerTests
{
    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly AnswerDueCarrierBookingsHandler _handler;

    public AnswerDueCarrierBookingsHandlerTests()
    {
        _personas.Find(Samples.KesslerSpedition.Id).Returns(Samples.KesslerSpedition);
        _handler = new AnswerDueCarrierBookingsHandler(_repository, _personas, _mediator, _time, new FakeLogger<AnswerDueCarrierBookingsHandler>());
    }

    private static CarrierBookingRequest Request(ScheduledAnswer? scheduled) => CarrierBookingRequest.Receive(
        Samples.KesslerSpedition.Id, "CB-0000042", "SH-0000042", "release-1", Samples.Now.AddSeconds(-5),
        new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []),
        scheduled);

    private static ScheduledAnswer Due(CarrierAnswerMode mode = CarrierAnswerMode.AutoConfirm, CarrierRejectionReason reason = CarrierRejectionReason.NoCapacity) =>
        new(mode, reason, Samples.Now);

    private void DueRequests(params CarrierBookingRequest[] requests) =>
        _repository.ListAsync(Arg.Any<CarrierBookingRequestsDueForAutoAnswerSpec>(), Arg.Any<CancellationToken>()).Returns([.. requests]);

    private void NextDue(DateTimeOffset? at, CarrierBookingRequest? request = null) =>
        _repository.FirstOrDefaultAsync(Arg.Any<NextAutoAnswerSpec>(), Arg.Any<CancellationToken>())
            .Returns(request ?? (at is { } due ? Request(new ScheduledAnswer(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, due)) : null));

    private void NorviaTakes(CarrierBookingRequest request) =>
        _mediator.Send(Arg.Any<AnswerCarrierBookingCommand>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var closed = CarrierBookingRequest.Receive(request.PersonaId, request.CarrierBookingNumber, request.ShipmentNumber, "c", Samples.Now, request.Details);
            closed.Answer(callInfo.Arg<AnswerCarrierBookingCommand>().Answer, "a-1", Samples.Now);
            return Result.Success(new AnsweredCarrierBooking(CarrierBookingRequestView.From(closed), NorviaReply.Recorded("a-1")));
        });

    [Fact]
    public async Task Handle_ADueConfirm_SendsAConfirmationWithAGeneratedReference_ThroughTheAnswerUseCase()
    {
        var request = Request(Due());
        DueRequests(request);
        NextDue(null);
        NorviaTakes(request);

        await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AnswerCarrierBookingCommand>(command =>
                command.PersonaId == new PersonaId("kessler-spedition")
                && command.CarrierBookingNumber == "CB-0000042"
                && command.Answer.Equals(new CarrierAnswer.Confirmation("KES-260924-0042"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADueReject_SendsARejectionWithTheChosenReason()
    {
        var request = Request(Due(CarrierAnswerMode.AutoReject, CarrierRejectionReason.WindowNotFeasible));
        DueRequests(request);
        NextDue(null);
        NorviaTakes(request);

        await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AnswerCarrierBookingCommand>(command => command.Answer.Equals(new CarrierAnswer.Rejection(CarrierRejectionReason.WindowNotFeasible, null))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReportsWhenTheNextAnswerIsDue()
    {
        DueRequests();
        NextDue(Samples.Now.AddSeconds(8));

        var outcome = await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None);

        outcome.NextDueAt.Should().Be(Samples.Now.AddSeconds(8));
        await _mediator.DidNotReceive().Send(Arg.Any<AnswerCarrierBookingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNothingScheduled_ReportsNoNextAnswer()
    {
        DueRequests();
        NextDue(null);

        (await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None)).NextDueAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenNorviaDoesNotTakeTheAnswer_TriesAgainShortly()
    {
        var request = Request(Due());
        DueRequests(request);
        NextDue(null);
        var stillOpen = Request(Due());
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(stillOpen);
        _mediator.Send(Arg.Any<AnswerCarrierBookingCommand>(), Arg.Any<CancellationToken>()).Returns(
            Result.Success(new AnsweredCarrierBooking(CarrierBookingRequestView.From(stillOpen), NorviaReply.NotReceived(null))));

        await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None);

        stillOpen.AutoAnswerDueAt.Should().Be(Samples.Now + AnswerDueCarrierBookingsHandler.RetryAfter);
        await _repository.Received(1).UpdateAsync(stillOpen, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARequestAnsweredByHandInTheMeantime_IsLeftAlone()
    {
        var request = Request(Due());
        request.Answer(new CarrierAnswer.Confirmation("VDT-1"), null, Samples.Now);
        DueRequests(request);
        NextDue(null);

        await _handler.Handle(new AnswerDueCarrierBookingsCommand(), CancellationToken.None);

        await _mediator.DidNotReceive().Send(Arg.Any<AnswerCarrierBookingCommand>(), Arg.Any<CancellationToken>());
    }
}
