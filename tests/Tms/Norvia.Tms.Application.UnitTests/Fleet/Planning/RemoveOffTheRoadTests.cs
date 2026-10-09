using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning.RemoveOffTheRoad;
using Norvia.Tms.Domain.Fleet;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class RemoveOffTheRoadTests
{
    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task Handle_AnOpenDay_PutsTheVehicleBackOnTheRoad()
    {
        _fixture.OpenDays();
        var day = Monday.AddDays(1);
        var offTheRoad = OffTheRoadDay.Record(Truck, day, OffTheRoadReason.Repair, null);
        _fixture.StoredOffTheRoadDays.Add(offTheRoad);
        _fixture.Day(day).TakeOffTheRoad(Truck);

        var result = await Run(offTheRoad.Id);

        result.IsSuccess.Should().BeTrue();
        _fixture.StoredOffTheRoadDays.Should().BeEmpty();
        _fixture.Day(day).AvailableVehicles.Should().BeEquivalentTo([Van, Truck]);
    }

    [Fact]
    public async Task Handle_ADayBeforeToday_IsRefusedAsClosed()
    {
        var offTheRoad = OffTheRoadDay.Record(Truck, Monday.AddDays(-2), OffTheRoadReason.Repair, null);
        _fixture.StoredOffTheRoadDays.Add(offTheRoad);

        var result = await Run(offTheRoad.Id);

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.DayClosed));
        _fixture.StoredOffTheRoadDays.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AnUnknownDay_IsRefused()
    {
        var result = await Run(OffTheRoadDayId.New());

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.OffTheRoadDayNotFound));
    }

    private async Task<Result> Run(OffTheRoadDayId id) =>
        await new RemoveOffTheRoadHandler(_fixture.OffTheRoadDays, _fixture.Days, _fixture.Horizon, _fixture.Time)
            .Handle(new RemoveOffTheRoadCommand(id), CancellationToken.None);
}
