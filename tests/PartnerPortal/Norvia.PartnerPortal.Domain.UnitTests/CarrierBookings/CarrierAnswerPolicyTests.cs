using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierAnswerPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduleFor_Manual_SchedulesNothing() =>
        new CarrierAnswerPolicy(CarrierAnswerMode.Manual, CarrierRejectionReason.NoCapacity, TimeSpan.FromSeconds(5)).ScheduleFor(At).Should().BeNull();

    [Fact]
    public void ScheduleFor_AutoConfirm_IsDueAfterTheDelay() =>
        new CarrierAnswerPolicy(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, TimeSpan.FromSeconds(5)).ScheduleFor(At)
            .Should().Be(new ScheduledAnswer(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, At.AddSeconds(5)));

    [Fact]
    public void ScheduleFor_AutoReject_KeepsTheChosenReason() =>
        new CarrierAnswerPolicy(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, TimeSpan.Zero).ScheduleFor(At)
            .Should().Be(new ScheduledAnswer(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, At));

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void Policy_RejectsADelayOutsideTheLimits(int seconds)
    {
        var create = () => new CarrierAnswerPolicy(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, TimeSpan.FromSeconds(seconds));

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Policy_RejectsAnUnknownMode()
    {
        var create = () => new CarrierAnswerPolicy((CarrierAnswerMode)99, CarrierRejectionReason.NoCapacity, TimeSpan.Zero);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ScheduledAnswer_RefusesManual()
    {
        var create = () => new ScheduledAnswer(CarrierAnswerMode.Manual, CarrierRejectionReason.NoCapacity, At);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
