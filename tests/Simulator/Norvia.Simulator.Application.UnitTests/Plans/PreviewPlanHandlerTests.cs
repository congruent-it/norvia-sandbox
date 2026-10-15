using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Plans;

public sealed class PreviewPlanHandlerTests
{
    private readonly IReadRepository<RunSequence> _sequences = Substitute.For<IReadRepository<RunSequence>>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();

    public PreviewPlanHandlerTests()
    {
        _world.Load().Returns(TestData.World);
    }

    private PreviewPlanHandler Handler() => new(_sequences, _world, TestData.Limits);

    [Fact]
    public async Task Handle_Settings_GivesTheBookingsAPlanOfThoseSettingsWouldSend()
    {
        var settings = TestData.Settings(orders: 8, bad: 25, seed: 3);

        var result = await Handler().Handle(new PreviewPlanQuery(settings), CancellationToken.None);

        var expected = BookingPlanGenerator.Generate(settings, TestData.World, 1);
        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Select(row => row.Reference).Should().Equal(expected.Bookings.Select(booking => booking.Reference));
        result.Value.Rows.Count(row => row.Bad is not null).Should().Be(2);
        result.Value.PerShipper.Sum(share => share.Bookings).Should().Be(8);
    }

    [Fact]
    public async Task Handle_CarrierShares_ShowTheCarrierStageOfTheFunnelForTheShipmentsMeantForACarrier()
    {
        var settings = TestData.Settings(
            orders: 20, bad: 0, assigned: 100, ownFleet: 0,
            carriers: new Norvia.Simulator.Domain.Carriers.CarrierShares(ReleasePercent: 50, ConfirmPercent: 80, DeclinePercent: 10, TrackedPercent: 100, ExceptionPercent: 0, DeliveredPercent: 50, ProofOfDeliveryPercent: 100));

        var result = await Handler().Handle(new PreviewPlanQuery(settings), CancellationToken.None);

        var counts = result.Value.Carriers.Counts;
        result.Value.Carriers.Shipments.Should().Be(20);
        (counts.Released, counts.Confirmed, counts.Declined, counts.NoAnswer, counts.Delivered, counts.ProofsOfDelivery).Should().Be((10, 8, 1, 1, 4, 4));
    }

    [Fact]
    public async Task Handle_OrdersMeantForOwnFleetFirst_AreNotCountedForTheCarriers()
    {
        var settings = TestData.Settings(orders: 20, bad: 0, assigned: 100, ownFleet: 25);

        var result = await Handler().Handle(new PreviewPlanQuery(settings), CancellationToken.None);

        result.Value.Carriers.Shipments.Should().Be(15);
    }

    [Fact]
    public async Task Handle_PlannerShares_ShowTheAssignmentStageOfTheFunnel()
    {
        var settings = TestData.Settings(orders: 20, bad: 0, assigned: 50, ownFleet: 40, reassign: 20);

        var result = await Handler().Handle(new PreviewPlanQuery(settings), CancellationToken.None);

        result.Value.Planner.Should().Be(new PlannerPreview(Orders: 20, Assigned: 10, TryOwnFleet: 4, Carrier: 6, Reassigned: 2));
        result.Value.Rows.Count(row => row.Planner is not null).Should().Be(10);
        result.Value.Rows.Count(row => row.Reassign).Should().Be(2);
    }

    [Fact]
    public async Task Handle_BadBookingsNorviaWillNotAccept_AreNotOrdersToAssign()
    {
        var settings = TestData.Settings(orders: 20, bad: 100, assigned: 100);

        var result = await Handler().Handle(new PreviewPlanQuery(settings), CancellationToken.None);

        result.Value.Planner.Orders.Should().Be(result.Value.Rows.Count(row => row.Expected == ExpectedAnswer.Accepted));
        result.Value.Planner.Assigned.Should().Be(result.Value.Planner.Orders);
    }

    [Fact]
    public async Task Handle_ARunAlreadyDone_NumbersThePlanAfterIt()
    {
        var sequence = RunSequence.Create();
        sequence.Take();
        sequence.Take();
        _sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<RunSequence>>(), Arg.Any<CancellationToken>()).Returns(sequence);

        var result = await Handler().Handle(new PreviewPlanQuery(TestData.Settings(orders: 2)), CancellationToken.None);

        result.Value.RunNumber.Should().Be(3);
        result.Value.Rows.Select(row => row.Reference).Should().Equal("SIM-3-1", "SIM-3-2");
    }

    [Fact]
    public async Task Handle_MoreOrdersThanTheCap_IsInvalid()
    {
        var result = await Handler().Handle(new PreviewPlanQuery(TestData.Settings(orders: 51)), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("at most 50");
    }

    [Fact]
    public async Task Handle_Rows_DescribeTheRouteAndNeverTheConsignee()
    {
        var result = await Handler().Handle(new PreviewPlanQuery(TestData.Settings(orders: 5)), CancellationToken.None);

        result.Value.Rows.Should().OnlyContain(row => row.Route.Contains('→', StringComparison.Ordinal) && row.WeightKg > 0);
    }

    [Fact]
    public async Task Handle_NoShipperWithALane_IsInvalidNotAnException()
    {
        _world.Load().Returns(new Norvia.Simulator.Domain.Worlds.SimulationWorld([]));

        var result = await Handler().Handle(new PreviewPlanQuery(TestData.Settings()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorMessage.Should().Contain("Rate Card");
    }
}
