using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class RecordOffTheRoadTests
{
    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task Handle_SeveralOpenDays_TakeTheVehicleOutOfThoseDays()
    {
        _fixture.OpenDays();

        var result = await Run(new RecordOffTheRoadCommand(Truck, Monday.AddDays(1), Monday.AddDays(2), OffTheRoadReason.Repair, "Gearbox"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().Be(2);
        _fixture.StoredOffTheRoadDays.Should().HaveCount(2);
        _fixture.Day(Monday.AddDays(1)).AvailableVehicles.Should().Equal(Van);
        _fixture.Day(Monday.AddDays(2)).AvailableVehicles.Should().Equal(Van);
        _fixture.Day(Monday).AvailableVehicles.Should().BeEquivalentTo([Truck, Van]);
        await _fixture.OffTheRoadDays.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADayBeyondTheHorizon_IsAcceptedAndOnlyStored()
    {
        _fixture.OpenDays();

        var result = await Run(new RecordOffTheRoadCommand(Truck, Monday.AddDays(30), Monday.AddDays(30), OffTheRoadReason.Maintenance, null));

        result.IsSuccess.Should().BeTrue();
        _fixture.StoredOffTheRoadDays.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_TheSameDayAgain_RevisesTheRecord()
    {
        await Run(new RecordOffTheRoadCommand(Truck, Monday, Monday, OffTheRoadReason.Maintenance, null));

        await Run(new RecordOffTheRoadCommand(Truck, Monday, Monday, OffTheRoadReason.Repair, "Tyres"));

        _fixture.StoredOffTheRoadDays.Should().ContainSingle().Which.Reason.Should().Be(OffTheRoadReason.Repair);
    }

    [Theory]
    [InlineData(-1, 0, nameof(PlanningRefusalCode.DayClosed))]
    [InlineData(2, 1, nameof(PlanningRefusalCode.PeriodInvalid))]
    public async Task Handle_ADayBeforeTodayOrAnInvalidPeriod_IsRefused(int fromOffset, int toOffset, string code)
    {
        var result = await Run(new RecordOffTheRoadCommand(Truck, Monday.AddDays(fromOffset), Monday.AddDays(toOffset), OffTheRoadReason.Repair, null));

        PlanningEditsFixture.Code(result).Should().Be(code);
        _fixture.StoredOffTheRoadDays.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnUnknownVehicle_IsRefused()
    {
        var result = await Run(new RecordOffTheRoadCommand(new VehicleId("VEH-9999"), Monday, Monday, OffTheRoadReason.Repair, null));

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.VehicleNotFound));
    }

    [Fact]
    public async Task Handle_ATooLongRemark_IsRefused()
    {
        var result = await Run(new RecordOffTheRoadCommand(Truck, Monday, Monday, OffTheRoadReason.Repair, new string('x', 501)));

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.RemarkTooLong));
    }

    private async Task<Result<RecordOffTheRoadResult>> Run(RecordOffTheRoadCommand command) =>
        await new RecordOffTheRoadHandler(_fixture.OffTheRoadDays, _fixture.Days, _fixture.Vehicles, _fixture.Horizon, _fixture.Time)
            .Handle(command, CancellationToken.None);
}
