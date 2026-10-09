using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning.RecordAbsence;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class RecordAbsenceTests
{
    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task Handle_SeveralOpenDays_RemoveTheDriversShiftsAndSave()
    {
        _fixture.OpenDays();

        var result = await Handle(Monday.AddDays(1), Monday.AddDays(3));

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().Be(3);
        _fixture.StoredAbsences.Should().HaveCount(3);
        foreach (var offset in new[] { 1, 2, 3 })
        {
            _fixture.Day(Monday.AddDays(offset)).Shifts.Should().NotContain(shift => shift.DriverId == Anouk);
            _fixture.Day(Monday.AddDays(offset)).Shifts.Should().Contain(shift => shift.DriverId == Joost);
        }

        _fixture.Day(Monday).Shifts.Should().Contain(shift => shift.DriverId == Anouk);
        await _fixture.Absences.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADayBeyondTheHorizon_IsAcceptedAndOnlyStored()
    {
        _fixture.OpenDays();
        var far = Monday.AddDays(40);

        var result = await Handle(far, far.AddDays(1));

        result.IsSuccess.Should().BeTrue();
        _fixture.StoredAbsences.Select(absence => absence.Date).Should().Equal(far, far.AddDays(1));
        _fixture.StoredDays.Should().HaveCount(15);
    }

    [Fact]
    public async Task Handle_ADriverWhoseShiftALaterTripWouldUse_IsStillAccepted()
    {
        _fixture.OpenDays();

        var result = await Handle(Monday, Monday);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TheSameDayAgain_RevisesTheAbsenceInsteadOfDuplicatingIt()
    {
        _fixture.OpenDays();
        await Handle(Monday.AddDays(1), Monday.AddDays(1));

        await Run(new RecordAbsenceCommand(Anouk, Monday.AddDays(1), Monday.AddDays(1), AbsenceReason.Sickness, "Flu"));

        _fixture.StoredAbsences.Should().ContainSingle().Which.Reason.Should().Be(AbsenceReason.Sickness);
    }

    [Fact]
    public async Task Handle_ADayBeforeToday_IsRefusedAsClosed()
    {
        _fixture.OpenDays();

        var result = await Handle(Monday.AddDays(-1), Monday);

        Code(result).Should().Be(nameof(PlanningRefusalCode.DayClosed));
        _fixture.StoredAbsences.Should().BeEmpty();
        await _fixture.Absences.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownDriver_IsRefused()
    {
        var result = await Run(new RecordAbsenceCommand(new DriverId("DRV-9999"), Monday, Monday, AbsenceReason.Leave, null));

        Code(result).Should().Be(nameof(PlanningRefusalCode.DriverNotFound));
    }

    [Fact]
    public async Task Handle_APeriodThatEndsBeforeItStarts_IsRefused()
    {
        var result = await Handle(Monday.AddDays(2), Monday.AddDays(1));

        Code(result).Should().Be(nameof(PlanningRefusalCode.PeriodInvalid));
    }

    [Fact]
    public async Task Handle_APeriodLongerThanTheTmsRecordsAtOnce_IsRefused()
    {
        var result = await Handle(Monday, Monday.AddDays(AvailabilityPeriod.MaxDays));

        Code(result).Should().Be(nameof(PlanningRefusalCode.PeriodInvalid));
    }

    [Fact]
    public async Task Handle_ATooLongRemark_IsRefused()
    {
        var result = await Run(new RecordAbsenceCommand(Anouk, Monday, Monday, AbsenceReason.Leave, new string('x', AvailabilityPeriod.MaxRemarkLength + 1)));

        Code(result).Should().Be(nameof(PlanningRefusalCode.RemarkTooLong));
    }

    private Task<Result<RecordAbsenceResult>> Handle(DateOnly from, DateOnly to) =>
        Run(new RecordAbsenceCommand(Anouk, from, to, AbsenceReason.Leave, "Holiday"));

    private async Task<Result<RecordAbsenceResult>> Run(RecordAbsenceCommand command) =>
        (await new RecordAbsenceHandler(_fixture.Absences, _fixture.Days, _fixture.Drivers, _fixture.Horizon, _fixture.Time).Handle(command, CancellationToken.None));
}
