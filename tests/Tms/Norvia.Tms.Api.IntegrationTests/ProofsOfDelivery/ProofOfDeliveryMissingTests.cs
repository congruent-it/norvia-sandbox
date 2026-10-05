using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using static Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery.ProofOfDeliveryCalls;

namespace Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;

/// <summary>
/// A Planner can ask for the Delivered Shipments without a current Proof of delivery (P6). A Shipment is flagged missing
/// 48 hours after the event time of its Delivered Tracking event, and the flag clears when a Proof of delivery arrives.
/// It is only a flag and publishes nothing.
/// </summary>
[Trait("AppHost", "Own3")]
public sealed class ProofOfDeliveryMissingTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task List_ADeliveredShipmentWithoutProof_ListsWithItsCarrierAndDeliveredTime_NotFlaggedYet()
    {
        var (shipmentNumber, _) = await DeliveredAsync(_now.AddHours(-1));

        var listed = Listed(await tms.ListDeliveredWithoutProofAsync(_cancellationToken), shipmentNumber);

        listed["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        listed["deliveredAt"]!.GetValue<DateTimeOffset>().Should().Be(_now.AddHours(-1));
        listed["isProofOfDeliveryMissing"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task List_TheFlagAppearsAt48HoursAfterTheDeliveredEventTime_AndNotBefore()
    {
        var delivered = _now.AddHours(-1);
        var (shipmentNumber, _) = await DeliveredAsync(delivered);

        tms.SetClock(delivered.AddHours(48).AddSeconds(-1));
        Listed(await tms.ListDeliveredWithoutProofAsync(_cancellationToken), shipmentNumber)["isProofOfDeliveryMissing"]!.GetValue<bool>().Should().BeFalse();

        tms.SetClock(delivered.AddHours(48));
        Listed(await tms.ListDeliveredWithoutProofAsync(_cancellationToken), shipmentNumber)["isProofOfDeliveryMissing"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task List_AProofOfDeliveryArriving_TakesTheShipmentOffTheList_WhetherFlaggedOrNot()
    {
        var (flaggedNumber, flaggedReference) = await DeliveredAsync(_now.AddHours(-1));
        var (freshNumber, freshReference) = await DeliveredAsync(_now.AddHours(-1));
        tms.SetClock(_now.AddHours(48));
        Listed(await tms.ListDeliveredWithoutProofAsync(_cancellationToken), flaggedNumber)["isProofOfDeliveryMissing"]!.GetValue<bool>().Should().BeTrue();

        await tms.RecordedProofAsync(Metadata(flaggedReference, deliveryTime: _now.AddHours(-1)), [Cmr()], _cancellationToken);
        await tms.RecordedProofAsync(Metadata(freshReference, deliveryTime: _now.AddHours(-1)), [Cmr()], _cancellationToken);

        var listed = (await tms.ListDeliveredWithoutProofAsync(_cancellationToken)).Select(entry => entry!["shipmentNumber"]!.GetValue<string>());
        listed.Should().NotContain([flaggedNumber, freshNumber]);
    }

    [Fact]
    public async Task List_ShipmentsNotDelivered_AreNotListed()
    {
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);

        (await tms.ListDeliveredWithoutProofAsync(_cancellationToken)).Select(entry => entry!["shipmentNumber"]!.GetValue<string>()).Should().NotContain(shipmentNumber);
    }

    [Fact]
    public async Task List_ListsFlaggedFirst_ThenOldestDeliveredFirst()
    {
        // Booked first, as a Transport Order's collection window must not be in the past when it is submitted.
        var (oldFlagged, oldFlaggedReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var (newerFlagged, newerFlaggedReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var (older, olderReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var (newer, newerReference) = await tms.BookedShipmentAsync(_cancellationToken);
        await DeliverAsync(oldFlaggedReference, _now.AddHours(-5));
        await DeliverAsync(newerFlaggedReference, _now.AddHours(-4));
        await DeliverAsync(olderReference, _now.AddHours(45));
        await DeliverAsync(newerReference, _now.AddHours(45).AddMinutes(30));
        tms.SetClock(_now.AddHours(50));

        var listed = (await tms.ListDeliveredWithoutProofAsync(_cancellationToken)).Select(entry => entry!["shipmentNumber"]!.GetValue<string>()).ToList();

        var mine = new[] { oldFlagged, newerFlagged, older, newer };
        listed.Where(number => mine.Contains(number)).Should().Equal(mine);
        listed.IndexOf(newerFlagged).Should().BeLessThan(listed.IndexOf(older), "flagged ones come first");
    }

    [Fact]
    public async Task List_TheFlag_PublishesNothing()
    {
        var (shipmentNumber, _) = await DeliveredAsync(_now.AddHours(-1));
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        var publishedBefore = tms.Published.Count();

        tms.SetClock(_now.AddHours(100));
        Listed(await tms.ListDeliveredWithoutProofAsync(_cancellationToken), shipmentNumber)["isProofOfDeliveryMissing"]!.GetValue<bool>().Should().BeTrue();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);

        tms.Published.Count().Should().Be(publishedBefore);
    }

    private async Task<(string ShipmentNumber, string CarrierBookingReference)> DeliveredAsync(DateTimeOffset eventTime)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await DeliverAsync(reference, eventTime);
        return (shipmentNumber, reference);
    }

    private async Task DeliverAsync(string reference, DateTimeOffset eventTime)
    {
        tms.SetClock(eventTime.AddMinutes(1));
        var delivered = await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, eventTime), _cancellationToken);
        delivered["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
    }

    private static JsonNode Listed(JsonArray listed, string shipmentNumber) =>
        listed.Single(entry => entry!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
}
