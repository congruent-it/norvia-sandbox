using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class AbsenceTests
{
    [Fact]
    public void Record_KeepsTheDriverDayReasonAndATrimmedRemark()
    {
        var absence = Absence.Record(new DriverId("DRV-0001"), new DateOnly(2026, 10, 20), AbsenceReason.Training, "  ADR course ");

        absence.DriverId.Should().Be(new DriverId("DRV-0001"));
        absence.Date.Should().Be(new DateOnly(2026, 10, 20));
        absence.Reason.Should().Be(AbsenceReason.Training);
        absence.Remark.Should().Be("ADR course");
    }

    [Fact]
    public void Revise_ChangesReasonAndRemark_AndNoRemarkIsEmpty()
    {
        var absence = Absence.Record(new DriverId("DRV-0001"), new DateOnly(2026, 10, 20), AbsenceReason.Leave, "Holiday");

        absence.Revise(AbsenceReason.Sickness, null);

        absence.Reason.Should().Be(AbsenceReason.Sickness);
        absence.Remark.Should().BeEmpty();
    }

    [Fact]
    public void OffTheRoadDay_RecordAndRevise_KeepTheVehicleDayReasonAndRemark()
    {
        var day = OffTheRoadDay.Record(new VehicleId("VEH-0001"), new DateOnly(2026, 10, 20), OffTheRoadReason.Maintenance, " Service ");

        day.Remark.Should().Be("Service");
        day.Revise(OffTheRoadReason.Repair, "Brakes");

        day.Reason.Should().Be(OffTheRoadReason.Repair);
        day.Remark.Should().Be("Brakes");
        day.VehicleId.Should().Be(new VehicleId("VEH-0001"));
        day.Date.Should().Be(new DateOnly(2026, 10, 20));
    }
}
