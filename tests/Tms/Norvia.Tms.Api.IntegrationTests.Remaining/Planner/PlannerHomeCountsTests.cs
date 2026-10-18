using static Norvia.Tms.TestSupport.Exceptions.ExceptionCalls;
using static Norvia.Tms.TestSupport.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.Planner;

/// <summary>
/// The counts of the Planner's home page (#521), read through the TMS UI's own adapter against the AppHost's SQL Server. The
/// class starts a TMS of its own. The data is made
/// through the TMS API, as the intake Logic App, the Carrier and the Planner make it.
/// </summary>
public sealed class PlannerHomeCountsTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counts_OfEachItem_AreTheLengthOfTheListItLinksTo()
    {
        await using var reader = await TmsQueryReader.OpenAsync(tms, _cancellationToken);
        var before = await reader.PlannerHomeAsync(_cancellationToken);

        // Two waiting for planning, one planned and so to release, one released Own fleet Shipment not on a Trip, one open Exception,
        // and one Delivered without a Proof of delivery.
        await tms.SubmitAcceptedAsync(_cancellationToken);
        await tms.SubmitAcceptedAsync(_cancellationToken);
        await tms.AssignPlannedAsync(await tms.SubmitAcceptedAsync(_cancellationToken), PlanningCalls.ToCarrier(), _cancellationToken);
        var ownFleet = await tms.AssignPlannedAsync(await tms.SubmitAcceptedAsync(_cancellationToken), PlanningCalls.ToOwnFleet(), _cancellationToken);
        await tms.ReleasedAsync(ownFleet["shipmentNumber"]!.GetValue<string>(), _cancellationToken);
        await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        var (_, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await tms.RecordedAsync(Event("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);

        var after = await reader.PlannerHomeAsync(_cancellationToken);

        after.WaitingForPlanning.Should().Be(before.WaitingForPlanning + 2);
        after.ShipmentsToRelease.Should().Be(before.ShipmentsToRelease + 1);
        after.OpenExceptions.Should().Be(before.OpenExceptions + 1);
        after.DeliveredWithoutProofOfDelivery.Should().Be(before.DeliveredWithoutProofOfDelivery + 1);
        after.ShipmentsToPutOnTrips.Should().Be(before.ShipmentsToPutOnTrips + 1);
        after.OverdueBookings.Should().Be(before.OverdueBookings, "the Carrier bookings made are not awaiting an answer for long");
    }
}
