using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestAnswerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static CarrierBookingRequest Open() => CarrierBookingRequest.Receive(
        new PersonaId("van-dijk-transport"), "CB-0000001", "SH-0000042", "release-1", At.AddHours(-1),
        new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null), null, []));

    [Fact]
    public void Receive_StartsOpen()
    {
        var request = Open();

        request.Status.Should().Be(CarrierBookingRequestStatus.Open);
        request.IsOpen.Should().BeTrue();
        request.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void Answer_AConfirmation_ConfirmsTheRequest_KeepingTheReferenceAndThePlannedTime()
    {
        var request = Open();
        var planned = new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2));

        var answered = request.Answer(new CarrierAnswer.Confirmation("VDT-77812", planned), "answer-1", At);

        answered.Should().BeTrue();
        request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        request.CarrierBookingReference.Should().Be("VDT-77812");
        request.PlannedCollection.Should().Be(planned);
        request.ClosedAt.Should().Be(At);
        request.AnswerCorrelationId.Should().Be("answer-1");
        request.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Answer_ARejection_RejectsTheRequest_KeepingTheReason()
    {
        var request = Open();

        request.Answer(new CarrierAnswer.Rejection(CarrierRejectionReason.Other, "No trucks on Friday"), "answer-1", At).Should().BeTrue();

        request.Status.Should().Be(CarrierBookingRequestStatus.Rejected);
        request.RejectionReason.Should().Be(CarrierRejectionReason.Other);
        request.RejectionText.Should().Be("No trucks on Friday");
        request.CarrierBookingReference.Should().BeNull();
    }

    [Fact]
    public void Answer_AnAnswerNorviaGaveNoMessageIdFor_StillClosesTheRequest()
    {
        var request = Open();

        request.Answer(new CarrierAnswer.Confirmation("R-1"), answerCorrelationId: null, At).Should().BeTrue();

        request.AnswerCorrelationId.Should().BeNull();
    }

    [Fact]
    public void Answer_ARequestThatIsAlreadyClosed_ChangesNothing()
    {
        var request = Open();
        request.Answer(new CarrierAnswer.Confirmation("R-1"), "answer-1", At);

        var answered = request.Answer(new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity), "answer-2", At.AddHours(1));

        answered.Should().BeFalse();
        request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        request.RejectionReason.Should().BeNull();
        request.AnswerCorrelationId.Should().Be("answer-1");
        request.ClosedAt.Should().Be(At);
    }

    [Fact]
    public void Answer_WithoutAnAnswer_Throws()
    {
        var answer = () => Open().Answer(null!, "answer-1", At);

        answer.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithdrawnByNorvia_AnOpenRequest_ShowsAsWithdrawn()
    {
        var request = Open();

        request.WithdrawnByNorvia("answer-1", At).Should().BeTrue();

        request.Status.Should().Be(CarrierBookingRequestStatus.WithdrawnByNorvia);
        request.ClosedAt.Should().Be(At);
        request.AnswerCorrelationId.Should().Be("answer-1");
    }

    [Fact]
    public void WithdrawnByNorvia_ARequestTheCarrierAlreadyAnswered_StaysAnswered()
    {
        var request = Open();
        request.Answer(new CarrierAnswer.Confirmation("R-1"), "answer-1", At);

        request.WithdrawnByNorvia("answer-2", At.AddHours(1)).Should().BeFalse();

        request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
    }
}
