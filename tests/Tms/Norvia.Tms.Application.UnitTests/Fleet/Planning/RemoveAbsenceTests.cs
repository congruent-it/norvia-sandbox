using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning.RemoveAbsence;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class RemoveAbsenceTests
{
    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task Handle_AnAbsenceOnAnOpenDay_GivesTheDriverTheShiftFromTheirRosterBack()
    {
        _fixture.OpenDays();
        var day = Monday.AddDays(1);
        var absence = Absence.Record(Anouk, day, AbsenceReason.Leave, null);
        _fixture.StoredAbsences.Add(absence);
        _fixture.Day(day).RecordAbsence(Anouk);

        var result = await Run(absence.Id);

        result.IsSuccess.Should().BeTrue();
        _fixture.StoredAbsences.Should().BeEmpty();
        _fixture.Day(day).Shifts.Should().Contain(new Shift(Anouk, new TimeOnly(6, 0), new TimeOnly(14, 0), false));
        await _fixture.Absences.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnAbsenceBeyondTheHorizon_IsJustDeleted()
    {
        var absence = Absence.Record(Anouk, Monday.AddDays(40), AbsenceReason.Training, null);
        _fixture.StoredAbsences.Add(absence);

        var result = await Run(absence.Id);

        result.IsSuccess.Should().BeTrue();
        _fixture.StoredAbsences.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnAbsenceBeforeToday_IsRefusedAsClosed()
    {
        var absence = Absence.Record(Anouk, Monday.AddDays(-1), AbsenceReason.Leave, null);
        _fixture.StoredAbsences.Add(absence);

        var result = await Run(absence.Id);

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.DayClosed));
        _fixture.StoredAbsences.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AnUnknownAbsence_IsRefused()
    {
        var result = await Run(AbsenceId.New());

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.AbsenceNotFound));
    }

    private async Task<Result> Run(AbsenceId id) =>
        await new RemoveAbsenceHandler(_fixture.Absences, _fixture.Days, _fixture.Rosters, _fixture.Horizon, _fixture.Time)
            .Handle(new RemoveAbsenceCommand(id), CancellationToken.None);
}
