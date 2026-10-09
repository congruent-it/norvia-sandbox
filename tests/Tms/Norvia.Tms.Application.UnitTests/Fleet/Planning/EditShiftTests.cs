using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning.EditShift;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class EditShiftTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DriverId Anouk = new("DRV-0001");

    private readonly List<PlanningDay> _stored = [];
    private readonly IRepository<PlanningDay> _days = Substitute.For<IRepository<PlanningDay>>();
    private readonly List<Absence> _storedAbsences = [];
    private readonly IRepository<Absence> _absences;
    private readonly IDriverDirectory _drivers = Substitute.For<IDriverDirectory>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2)));

    public EditShiftTests()
    {
        _absences = InMemoryRepository.Over(_storedAbsences);
        _drivers.ListActive().Returns([new Driver(Anouk, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.B }, Active: true)]);
        _days.FirstOrDefaultAsync(Arg.Any<ISpecification<PlanningDay>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<PlanningDay>>().Evaluate(_stored).FirstOrDefault());
    }

    [Fact]
    public async Task Handle_AnOpenDay_SetsTheShiftAndSaves()
    {
        _stored.Add(PlanningDay.Open(Monday, [], []));

        var result = await Handle(Monday, Anouk);

        result.IsSuccess.Should().BeTrue();
        _stored.Single().Shifts.Should().ContainSingle().Which.EditedByHand.Should().BeTrue();
        await _days.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADriverWithAnAbsenceThatDay_IsRefused_AndGetsNoShift()
    {
        _stored.Add(PlanningDay.Open(Monday, [], []));
        _storedAbsences.Add(Absence.Record(Anouk, Monday, AbsenceReason.Leave, null));

        var result = await Handle(Monday, Anouk);

        Refusal(result).Should().Be(PlanningRefusalCode.DriverAbsent.ToString());
        _stored.Single().Shifts.Should().BeEmpty();
        await _days.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADayBeforeToday_IsRefusedAsClosed_EvenBeforeTheHorizonJobClosedIt()
    {
        _stored.Add(PlanningDay.Open(Monday.AddDays(-1), [], []));

        var result = await Handle(Monday.AddDays(-1), Anouk);

        Refusal(result).Should().Be(PlanningRefusalCode.DayClosed.ToString());
        _stored.Single().Shifts.Should().BeEmpty();
        await _days.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADayTheHorizonDoesNotHave_IsRefusedAsNotFound()
    {
        var result = await Handle(Monday.AddDays(30), Anouk);

        Refusal(result).Should().Be(PlanningRefusalCode.PlanningDayNotFound.ToString());
    }

    [Fact]
    public async Task Handle_ADriverTheOwnFleetDoesNotHave_IsRefused()
    {
        _stored.Add(PlanningDay.Open(Monday, [], []));

        var result = await Handle(Monday, new DriverId("DRV-9999"));

        Refusal(result).Should().Be(PlanningRefusalCode.DriverNotFound.ToString());
    }

    private async Task<Ardalis.Result.Result<EditShiftResult>> Handle(DateOnly date, DriverId driverId) =>
        await new EditShiftHandler(_days, _absences.AsRead(), _drivers, new PlanningHorizonRules(14, Amsterdam), _time)
            .Handle(new EditShiftCommand(date, driverId, new TimeOnly(5, 0), new TimeOnly(13, 0)), CancellationToken.None);

    private static string Refusal(Ardalis.Result.Result<EditShiftResult> result) => result.ValidationErrors.Single().ErrorCode;
}
