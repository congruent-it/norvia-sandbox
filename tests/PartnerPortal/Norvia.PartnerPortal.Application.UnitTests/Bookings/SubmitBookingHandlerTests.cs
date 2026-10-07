using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Bookings;

public sealed class SubmitBookingHandlerTests
{
    private readonly IRepository<Booking> _repository = Substitute.For<IRepository<Booking>>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly FakeLogger<SubmitBookingHandler> _logger = new();
    private readonly SubmitBookingHandler _handler;

    public SubmitBookingHandlerTests()
    {
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _handler = new SubmitBookingHandler(_repository, _partnerApi, _personas, _time, _logger);
    }

    [Fact]
    public async Task Handle_BookingNorviaAccepts_ReturnsTheTransportOrderNumber()
    {
        _partnerApi.SubmitAsync(Samples.BrouwerDranken, Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>())
            .Returns(NorviaAnswer.Accepted("TO-0000042", "corr-1"));

        var result = await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Result.Should().Be(BookingResult.Accepted);
        result.Value.TransportOrderNumber.Should().Be("TO-0000042");
        result.Value.MessageId.Should().Be("corr-1");
        result.Value.ShipperReference.Should().Be("BD-2030-0001");
    }

    [Fact]
    public async Task Handle_AnyAnswer_SendsTheBookingAsThePersona()
    {
        var booking = Samples.Booking();
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(NorviaAnswer.NotReceived(null));

        await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), booking), CancellationToken.None);

        await _partnerApi.Received(1).SubmitAsync(Samples.BrouwerDranken, booking, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookingNorviaRejects_ReturnsEveryReason()
    {
        RejectionReason[] reasons = [new("QuantityInvalid", "Goods line 1 needs a quantity of at least 1."), new("WeightInvalid", "Goods line 1 needs a weight greater than 0 kg.")];
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(NorviaAnswer.Rejected(reasons, "corr-2"));

        var result = await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking()), CancellationToken.None);

        result.Value.Result.Should().Be(BookingResult.Rejected);
        result.Value.Reasons.Should().Equal(reasons);
        result.Value.TransportOrderNumber.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(EveryAnswer))]
    public async Task Handle_WhateverNorviaAnswers_RecordsTheBookingWithTheAnswer(NorviaAnswer answer)
    {
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(answer);

        await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking("BD-7")), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<Booking>(booking =>
                booking.PersonaId == Samples.BrouwerDranken.Id
                && booking.ShipperReference == "BD-7"
                && booking.SentAt == Samples.Now
                && booking.Answer == answer),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(EveryAnswer))]
    public async Task Handle_AnswerThatWasRecorded_SaysSo(NorviaAnswer answer)
    {
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(answer);

        var result = await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking()), CancellationToken.None);

        result.Value.Recorded.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheBookingCantBeRecorded_StillReturnsNorviasAnswer_AsNotRecorded()
    {
        // Norvia's answer stands whatever happens to the portal's own record of it: here, a database still resuming.
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>())
            .Returns(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        _repository.AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>()).Returns<Booking>(_ => throw new InvalidOperationException("Connection Timeout Expired."));

        var result = await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Result.Should().Be(BookingResult.Accepted);
        result.Value.TransportOrderNumber.Should().Be("TO-0000042");
        result.Value.MessageId.Should().Be("corr-1");
        result.Value.Recorded.Should().BeFalse();
        _logger.LatestRecord.Level.Should().Be(LogLevel.Error);
        _logger.LatestRecord.Exception.Should().BeOfType<InvalidOperationException>();
        _logger.LatestRecord.Scopes.OfType<IReadOnlyDictionary<string, object?>>().Single()
            .Should().Contain("correlationId", "corr-1").And.Contain("flow", "transport-order-intake").And.Contain("partyId", "SHP-0001");
    }

    [Fact]
    public async Task Handle_CancelledWhileRecording_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        _partnerApi.SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(NorviaAnswer.NotReceived(null));
        _repository.AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>()).Returns<Booking>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var handling = async () => await _handler.Handle(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), Samples.Booking()), cancellation.Token);

        await handling.Should().ThrowAsync<OperationCanceledException>();
    }

    public static TheoryData<NorviaAnswer> EveryAnswer() =>
    [
        NorviaAnswer.Accepted("TO-0000042", "corr-1"),
        NorviaAnswer.AlreadyBooked("TO-0000042", "corr-1"),
        NorviaAnswer.Rejected([new RejectionReason("QuantityInvalid", "Goods line 1 needs a quantity of at least 1.")], "corr-1"),
        NorviaAnswer.NotReceived(null),
        NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-1"),
    ];

    [Fact]
    public async Task Handle_UnknownPersona_IsNotFound_AndSendsNothing()
    {
        var result = await _handler.Handle(new SubmitBookingCommand(new PersonaId("nobody"), Samples.Booking()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _partnerApi.DidNotReceive().SubmitAsync(Arg.Any<Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
    }
}
