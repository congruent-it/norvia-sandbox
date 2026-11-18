using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.TrackingEvents;

/// <summary>
/// The TMS can answer what is on its way and what each Carrier actually reported (P5): the Shipments in progress with their
/// last Tracking event and ETA, and a Shipment's timeline of Tracking events.
/// </summary>
public sealed class TrackingQueryTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task InProgress_ABookedShipmentNoCarrierHasReportedOn_ListsWithNoLastEvent_AndTheDeliveryWindowAsItsEta()
    {
        var deliveryLatest = DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16);
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);

        var listed = InProgressEntry(await tms.ListShipmentsInProgressAsync(_cancellationToken), shipmentNumber);

        listed["status"]!.GetValue<string>().Should().Be("Booked");
        listed["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        listed["lastTrackingEvent"].Should().BeNull();
        listed["eta"]!.GetValue<DateTimeOffset>().Should().Be(deliveryLatest);
    }

    [Fact]
    public async Task InProgress_ACollectedAndInTransitShipment_ShowsItsLastEvent_AndTheLastEtaACarrierReported()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Collected(reference, _now.AddHours(-3), eta: _now.AddDays(2)), _cancellationToken);
        tms.SetClock(_now.AddMinutes(1));
        await tms.RecordedAsync(Event("Departed", reference, _now.AddHours(-2)), _cancellationToken);
        var revisedEta = _now.AddDays(1);
        tms.SetClock(_now.AddMinutes(2));
        await tms.RecordedAsync(Event("Arrived", reference, _now.AddHours(-1), eta: revisedEta), _cancellationToken);

        var listed = InProgressEntry(await tms.ListShipmentsInProgressAsync(_cancellationToken), shipmentNumber);

        listed["status"]!.GetValue<string>().Should().Be("InTransit");
        var last = listed["lastTrackingEvent"]!;
        last["type"]!.GetValue<string>().Should().Be("Arrived");
        last["eventTime"]!.GetValue<DateTimeOffset>().Should().Be(_now.AddHours(-1));
        last["place"]!.GetValue<string>().Should().Be("Rotterdam");
        listed["eta"]!.GetValue<DateTimeOffset>().Should().Be(revisedEta);
    }

    [Fact]
    public async Task InProgress_AShipmentSilentForADay_IsStale_AndClearsWhenAnotherEventArrives()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Collected(reference, _now.AddHours(-3)), _cancellationToken);
        InProgressEntry(await tms.ListShipmentsInProgressAsync(_cancellationToken), shipmentNumber)["isStale"]!.GetValue<bool>().Should().BeFalse();

        tms.SetClock(_now.AddHours(24));
        InProgressEntry(await tms.ListShipmentsInProgressAsync(_cancellationToken), shipmentNumber)["isStale"]!.GetValue<bool>().Should().BeTrue();

        await tms.RecordedAsync(Event("Departed", reference, _now.AddHours(24).AddMinutes(-1)), _cancellationToken);
        InProgressEntry(await tms.ListShipmentsInProgressAsync(_cancellationToken), shipmentNumber)["isStale"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task InProgress_ADeliveredShipment_LeavesTheList()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Collected(reference, _now.AddHours(-3)), _cancellationToken);
        (await tms.ListShipmentsInProgressAsync(_cancellationToken)).Select(entry => entry!["shipmentNumber"]!.GetValue<string>()).Should().Contain(shipmentNumber);

        await tms.RecordedAsync(Event("Delivered", reference, _now.AddHours(-1)), _cancellationToken);

        (await tms.ListShipmentsInProgressAsync(_cancellationToken)).Select(entry => entry!["shipmentNumber"]!.GetValue<string>()).Should().NotContain(shipmentNumber);
    }

    [Fact]
    public async Task Timeline_ListsTheEventsInEventTimeOrder_WithArrivalAndWhetherEachMovedTheStatus()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Event("Departed", reference, _now.AddHours(-2)), _cancellationToken);
        tms.SetClock(_now.AddMinutes(1));
        await tms.RecordedAsync(Collected(reference, _now.AddHours(-3)), _cancellationToken);
        tms.SetClock(_now.AddMinutes(2));
        await tms.RecordedAsync(Event("Delivered", reference, _now.AddHours(-1)), _cancellationToken);

        var timeline = await tms.GetTrackingTimelineAsync(shipmentNumber, _cancellationToken);

        timeline.Select(entry => entry!["type"]!.GetValue<string>()).Should().Equal("Collected", "Departed", "Delivered");
        timeline.Select(entry => entry!["movedShipment"]!.GetValue<bool>()).Should().Equal(false, true, true);
        timeline.Select(entry => entry!["receivedAt"]!.GetValue<DateTimeOffset>()).Should().Equal(_now.AddMinutes(1), _now, _now.AddMinutes(2));
        timeline[0]!["place"]!.GetValue<string>().Should().Be("Rotterdam");
    }

    [Fact]
    public async Task Timeline_AShipmentTheTmsDoesntHave_IsNotFound()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/shipments/SH-9999999/tracking-events", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static JsonNode InProgressEntry(JsonArray listed, string shipmentNumber) =>
        listed.Single(entry => entry!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
}
