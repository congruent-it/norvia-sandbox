using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListToPutOnTrips;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Web.Planner;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Planner;

public sealed class PlannerHomeReaderTests
{
    private readonly ITransportOrderQueries _queries = Substitute.For<ITransportOrderQueries>();

    [Fact]
    public async Task ReadAsync_CountsEachListItLinksTo()
    {
        GivenLists(waiting: 3, toRelease: 2, overdue: 1, notOverdue: 4, exceptions: 5, withoutProof: 6, toPutOnTrips: 7);

        var counts = Assert.Single(await PlannerHomeReader.ReadAsync(_queries, Xunit.TestContext.Current.CancellationToken));

        counts.Should().Be(new PlannerHomeCounts(3, 2, 1, 5, 6, 7));
    }

    [Fact]
    public async Task ReadAsync_WithNothingToDo_CountsZeroEverywhere()
    {
        GivenLists(0, 0, 0, 0, 0, 0, 0);

        var counts = Assert.Single(await PlannerHomeReader.ReadAsync(_queries, Xunit.TestContext.Current.CancellationToken));

        counts.Should().Be(new PlannerHomeCounts(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task ReadAsync_WhenAListCantBeRead_Fails()
    {
        GivenLists(1, 1, 0, 0, 1, 1, 1);
        _queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<OpenExceptionSummary>>(new InvalidOperationException("down")));

        var read = () => PlannerHomeReader.ReadAsync(_queries, Xunit.TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    private void GivenLists(int waiting, int toRelease, int overdue, int notOverdue, int exceptions, int withoutProof, int toPutOnTrips)
    {
        _queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(new TransportOrderToPlanSummary[waiting]);
        _queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(new ShipmentToReleaseSummary[toRelease]);
        _queries.ListCarrierBookingsAwaitingAnswerAsync(Arg.Any<CancellationToken>()).Returns(
            [.. Enumerable.Repeat(Booking(true), overdue), .. Enumerable.Repeat(Booking(false), notOverdue)]);
        _queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(new OpenExceptionSummary[exceptions]);
        _queries.ListDeliveredShipmentsWithoutProofOfDeliveryAsync(Arg.Any<CancellationToken>()).Returns(new DeliveredShipmentWithoutProofOfDeliverySummary[withoutProof]);
        _queries.ListShipmentsToPutOnTripsAsync(null, Arg.Any<CancellationToken>()).Returns(new ShipmentToPutOnTripSummary[toPutOnTrips]);
    }

    private static CarrierBookingAwaitingAnswerSummary Booking(bool overdue) =>
        new("SH-0000001", "CB-0000001", new("carrier-1", "Carrier"), DateTimeOffset.UnixEpoch, null, null, overdue);
}
