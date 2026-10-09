using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.ListOpenDays;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class ListOpenPlanningDaysTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public async Task Handle_OpenAndClosedDays_ListsOneRowPerOpenDay_WithShiftsByStartThenName()
    {
        var anouk = new DriverId("DRV-0001");
        var joost = new DriverId("DRV-0002");
        var roster = (DriverId id, int hour) => new Roster(id, new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Monday] = new(new TimeOnly(hour, 0), new TimeOnly(hour + 8, 0)) });
        var closed = PlanningDay.Open(Monday.AddDays(-7), [roster(anouk, 6)], []);
        closed.Close();
        var stored = new List<PlanningDay> { closed, PlanningDay.Open(Monday, [roster(joost, 6), roster(anouk, 6)], [new VehicleId("VEH-0001"), new VehicleId("VEH-0002")]) };
        var days = Substitute.For<IReadRepository<PlanningDay>>();
        days.ListAsync(Arg.Any<ISpecification<PlanningDay>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<PlanningDay>>().Evaluate(stored).ToList());
        var drivers = Substitute.For<IDriverDirectory>();
        drivers.ListActive().Returns(
        [
            new Driver(anouk, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.B }, true),
            new Driver(joost, "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.B }, true),
        ]);

        var listed = await new ListOpenPlanningDaysHandler(days, drivers).Handle(new ListOpenPlanningDaysQuery(), CancellationToken.None);

        var day = listed.Should().ContainSingle().Subject;
        day.Date.Should().Be(Monday);
        day.AvailableVehicles.Should().Be(2);
        day.Shifts.Select(shift => shift.DriverName).Should().Equal("Anouk de Vries", "Joost Bakker");
        day.Shifts[0].Should().Be(new ShiftView("DRV-0001", "Anouk de Vries", new TimeOnly(6, 0), new TimeOnly(14, 0), false));
    }
}
