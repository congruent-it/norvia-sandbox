using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestAutoAnswerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static CarrierBookingRequest Receive(ScheduledAnswer? scheduled) => CarrierBookingRequest.Receive(
        new PersonaId("kessler-spedition"), "CB-0000042", "SH-0000042", "release-1", At,
        new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null), null, []),
        scheduled);

    private static ScheduledAnswer Confirm(int seconds = 5) => new(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, At.AddSeconds(seconds));

    [Fact]
    public void ARequestReceivedWithoutASchedule_HasNoAutoAnswer()
    {
        var request = Receive(null);

        request.AutoAnswerMode.Should().BeNull();
        request.AutoAnswerDue("KES", At.AddHours(1)).Should().BeNull();
    }

    [Fact]
    public void ARequestReceivedWithASchedule_KeepsIt()
    {
        var request = Receive(new ScheduledAnswer(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, At.AddSeconds(5)));

        request.AutoAnswerMode.Should().Be(CarrierAnswerMode.AutoReject);
        request.AutoAnswerReason.Should().Be(CarrierRejectionReason.LaneNotServed);
        request.AutoAnswerDueAt.Should().Be(At.AddSeconds(5));
    }

    [Fact]
    public void AutoAnswerDue_BeforeTheDelay_IsNull() =>
        Receive(Confirm()).AutoAnswerDue("KES", At.AddSeconds(4)).Should().BeNull();

    [Fact]
    public void AutoAnswerDue_ForAConfirm_ConfirmsWithAGeneratedReference()
    {
        var answer = Receive(Confirm()).AutoAnswerDue("KES", At.AddSeconds(5));

        answer.Should().BeEquivalentTo(new CarrierAnswer.Confirmation("KES-261002-0042"));
    }

    [Fact]
    public void AutoAnswerDue_ForAReject_RejectsWithTheChosenReason()
    {
        var request = Receive(new ScheduledAnswer(CarrierAnswerMode.AutoReject, CarrierRejectionReason.GoodsNotAccepted, At));

        request.AutoAnswerDue("KES", At).Should().Be(new CarrierAnswer.Rejection(CarrierRejectionReason.GoodsNotAccepted));
    }

    [Fact]
    public void AutoAnswerDue_ForAReasonOther_SaysItWasAutomatic()
    {
        var request = Receive(new ScheduledAnswer(CarrierAnswerMode.AutoReject, CarrierRejectionReason.Other, At));

        request.AutoAnswerDue("KES", At).Should().BeOfType<CarrierAnswer.Rejection>().Which.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AnsweringByHand_EndsTheSchedule()
    {
        var request = Receive(Confirm());

        request.Answer(new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity), "answer-1", At.AddSeconds(1));

        request.AutoAnswerDueAt.Should().BeNull();
        request.AutoAnswerDue("KES", At.AddHours(1)).Should().BeNull();
    }

    [Fact]
    public void WithdrawnByNorvia_EndsTheSchedule()
    {
        var request = Receive(Confirm());

        request.WithdrawnByNorvia("answer-1", At.AddSeconds(1));

        request.AutoAnswerDueAt.Should().BeNull();
    }

    [Fact]
    public void DeferAutoAnswer_MovesTheDueTime()
    {
        var request = Receive(Confirm());

        request.DeferAutoAnswer(At.AddSeconds(15));

        request.AutoAnswerDueAt.Should().Be(At.AddSeconds(15));
        request.AutoAnswerDue("KES", At.AddSeconds(10)).Should().BeNull();
    }

    [Fact]
    public void DeferAutoAnswer_OnARequestAnsweredByHand_ChangesNothing()
    {
        var request = Receive(Confirm());
        request.Answer(new CarrierAnswer.Confirmation("KES-1"), null, At);

        request.DeferAutoAnswer(At.AddSeconds(15));

        request.AutoAnswerDueAt.Should().BeNull();
    }
}
