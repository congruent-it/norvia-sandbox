using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.ShipperTracking.ShipperTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ShipperTracking;

/// <summary>
/// A Shipper lists its Transport Orders with their Tracking status straight from the TMS (P8). The status is worked out when the
/// list is asked for, and the list shows nothing of the Carrier, the Shipment number, Exceptions or Stale.
/// </summary>
public sealed class ShipperTrackingListTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_AnAcceptedTransportOrderWithNoShipment_IsReceived_WithItsNumberReferencePlacesAndEta()
    {
        var reference = TmsCalls.NewReference();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(reference), TmsCalls.NewCorrelationId(), _cancellationToken);

        var listed = await tms.ListedAsync(Shipper, number, _cancellationToken);

        listed["trackingStatus"]!.GetValue<string>().Should().Be("Received");
        listed["shipperReference"]!.GetValue<string>().Should().Be(reference);
        listed["collection"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
        listed["collection"]!["countryCode"]!.GetValue<string>().Should().Be("NL");
        listed["delivery"]!["city"]!.GetValue<string>().Should().Be("Utrecht");
        listed["eta"]!.GetValue<DateTimeOffset>().Should().Be(DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16));
        listed["lastUpdate"].Should().BeNull();
        var results = CanonicalModelSchemas.ValidateShipperTransportOrderListItem(listed);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
    }

    [Fact]
    public async Task List_APlannedAndAReleasedShipment_AreScheduled()
    {
        var planned = await tms.SubmitAcceptedAsync(_cancellationToken);
        var assigned = await tms.AssignPlannedAsync(planned, PlanningCalls.ToCarrier(), _cancellationToken);
        (await tms.TrackingStatusAsync(planned, _cancellationToken)).Should().Be("Scheduled");

        await tms.ReleasedAsync(assigned["shipmentNumber"]!.GetValue<string>(), _cancellationToken);

        (await tms.TrackingStatusAsync(planned, _cancellationToken)).Should().Be("Scheduled");
    }

    [Fact]
    public async Task List_AShipmentThroughItsJourney_FollowsItsStatus_AndTheLastUpdateIsTheLatestProgressEvent()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        (await tms.TrackingStatusAsync(number, _cancellationToken)).Should().Be("Scheduled");
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await tms.RecordedAsync(TrackingEventCalls.Collected(reference, now.AddHours(-4)), _cancellationToken);
        var collected = await tms.ListedAsync(Shipper, number, _cancellationToken);
        collected["trackingStatus"]!.GetValue<string>().Should().Be("Collected");
        collected["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-4));

        await tms.RecordedAsync(TrackingEventCalls.Event("Departed", reference, now.AddHours(-3), eta: now.AddHours(5)), _cancellationToken);
        var inTransit = await tms.ListedAsync(Shipper, number, _cancellationToken);
        inTransit["trackingStatus"]!.GetValue<string>().Should().Be("InTransit");
        inTransit["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-3));
        inTransit["eta"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(5));

        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, now.AddHours(-1)), _cancellationToken);
        var delivered = await tms.ListedAsync(Shipper, number, _cancellationToken);
        delivered["trackingStatus"]!.GetValue<string>().Should().Be("Delivered");
        delivered["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-1));
    }

    [Fact]
    public async Task List_ACarrierBookingRejected_LeavesTheTrackingStatusScheduled()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);

        await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);

        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
        (await tms.TrackingStatusAsync(number, _cancellationToken)).Should().Be("Scheduled");
    }

    [Fact]
    public async Task List_ACarrierBookingCancelledAfterBooking_LeavesTheTrackingStatusScheduled()
    {
        var (shipmentNumber, _, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);

        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, ExceptionCalls.Cancellation(), _cancellationToken);

        cancelled.IsSuccessStatusCode.Should().BeTrue();
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
        (await tms.TrackingStatusAsync(number, _cancellationToken)).Should().Be("Scheduled");
    }

    [Fact]
    public async Task List_RejectedBookingsAndAnotherShippersTransportOrders_AreNotReturned()
    {
        var own = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        var anothers = await tms.SubmitAcceptedForAsync(AnotherShipper, _cancellationToken);
        await tms.SubmitRejectedAsync(Shipper, _cancellationToken);

        var ofShipper = await tms.ListedForAsync(Shipper, _cancellationToken);
        var ofAnother = await tms.ListedForAsync(AnotherShipper, _cancellationToken);

        Numbers(ofShipper).Should().Contain(own).And.NotContain(anothers);
        ofShipper.Should().OnlyContain(item => item!["transportOrderNumber"] != null, "a Rejected booking has no Transport Order number");
        Numbers(ofAnother).Should().Contain(anothers).And.NotContain(own);
    }

    [Fact]
    public async Task List_ByCollectionDate_ShowsTheNewestFirst()
    {
        var earlier = Bookings.Valid(TmsCalls.NewReference());
        var later = Bookings.Valid(TmsCalls.NewReference());
        var laterDay = DateTimeOffset.UtcNow.Date.AddDays(9);
        later["collection"]!["window"] = ExceptionCalls.Window(laterDay.AddHours(6), laterDay.AddHours(10));
        later["delivery"]!["window"] = ExceptionCalls.Window(laterDay.AddHours(12), laterDay.AddHours(16));
        var earlierNumber = await tms.SubmitAcceptedAsync(earlier, TmsCalls.NewCorrelationId(), _cancellationToken);
        var laterNumber = await tms.SubmitAcceptedAsync(later, TmsCalls.NewCorrelationId(), _cancellationToken);

        var numbers = Numbers(await tms.ListedForAsync(Shipper, _cancellationToken)).ToList();

        numbers.IndexOf(laterNumber).Should().BeLessThan(numbers.IndexOf(earlierNumber));
    }

    [Fact]
    public async Task List_FilteredByCollectionDatesAndStatus_AndPaged_OnlyCoversTheShippersOwnTransportOrders()
    {
        var day = DateTimeOffset.UtcNow.Date.AddDays(500);
        string[] mine = [await SubmitCollectingAsync(Shipper, day), await SubmitCollectingAsync(Shipper, day.AddDays(1))];
        var anothers = await SubmitCollectingAsync(AnotherShipper, day);
        var range = $"collectionFrom={day:yyyy-MM-dd}&collectionTo={day.AddDays(1):yyyy-MM-dd}";

        var both = await ListedAsync(Shipper, range);
        var onlyFirstDay = await ListedAsync(Shipper, $"collectionFrom={day:yyyy-MM-dd}&collectionTo={day:yyyy-MM-dd}");
        var secondPage = await ListedAsync(Shipper, $"{range}&pageSize=1&page=2");
        var scheduled = await ListedAsync(Shipper, $"{range}&trackingStatus=Scheduled");
        var received = await ListedAsync(Shipper, $"{range}&trackingStatus=Received");

        Numbers(both).Should().Equal(mine.Reverse());
        Numbers(onlyFirstDay).Should().Equal(mine[0]);
        Numbers(secondPage).Should().Equal(mine[0]);
        scheduled.Should().BeEmpty();
        Numbers(received).Should().Equal(mine.Reverse());
        Numbers(both).Should().NotContain(anothers);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("collectionFrom=2030-02-01&collectionTo=2030-01-01")]
    [InlineData("trackingStatus=Exception")]
    public async Task List_WithAnInvalidFilterOrPage_IsABadRequest(string query)
    {
        using var response = await tms.ListAsync(tms.ShipperTracking, Shipper, _cancellationToken, query: query);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private async Task<JsonArray> ListedAsync(string shipperId, string query)
    {
        using var response = await tms.ListAsync(tms.ShipperTracking, shipperId, _cancellationToken, query: query);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    private async Task<string> SubmitCollectingAsync(string shipperId, DateTimeOffset day)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        booking["shipperId"] = shipperId;
        booking["collection"]!["window"] = ExceptionCalls.Window(day.AddHours(6), day.AddHours(10));
        booking["delivery"]!["window"] = ExceptionCalls.Window(day.AddDays(1).AddHours(12), day.AddDays(1).AddHours(16));
        return await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
    }

    [Fact]
    public async Task List_ForAShipmentWithACarrierAndAnException_ShowsNoCarrierShipmentNumberExceptionOrStale()
    {
        var (shipmentNumber, _, _, _) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);

        var body = await tms.ListedBodyForAsync(Shipper, _cancellationToken);

        body.Should().Contain(number);
        body.Should().NotContainEquivalentOf(shipmentNumber).And.NotContainEquivalentOf("CAR-").And.NotContainEquivalentOf("carrier")
            .And.NotContainEquivalentOf("exception").And.NotContainEquivalentOf("stale").And.NotContainEquivalentOf("shipment");
        foreach (var item in JsonNode.Parse(body)!.AsArray())
        {
            var results = CanonicalModelSchemas.ValidateShipperTransportOrderListItem(item!);
            results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        }
    }

    private static IEnumerable<string> Numbers(JsonArray listed) => listed.Select(item => item!["transportOrderNumber"]!.GetValue<string>());
}
