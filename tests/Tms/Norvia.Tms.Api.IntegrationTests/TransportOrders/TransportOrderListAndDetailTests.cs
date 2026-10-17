using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders.Browse;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using static Norvia.Tms.Api.IntegrationTests.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// The TMS UI's list of Transport Orders and its detail page (#523), read by their query handlers against the AppHost's SQL Server. The data is made
/// through the TMS API, as the intake Logic App and the Planner make it. The tests share one database, so each tags the Shipper references it makes and
/// searches for the tag, which also exercises the search.
/// </summary>
[Trait("AppHost", "Own1")]
public sealed class TransportOrderListAndDetailTests(TransportOrderListAndDetailTests.BrowseTmsFixture tms)
    : IClassFixture<TransportOrderListAndDetailTests.BrowseTmsFixture>, IAsyncLifetime
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);
    private TmsQueryReader _reader = null!;

    /// <summary>A TMS of its own, whose clock these tests move for the Trip they put a Shipment on.</summary>
    public sealed class BrowseTmsFixture : ControlledClockTmsFixture;

    public async ValueTask InitializeAsync() => _reader = await TmsQueryReader.OpenAsync(tms, _cancellationToken);

    public async ValueTask DisposeAsync()
    {
        tms.ResetClock();
        await _reader.DisposeAsync();
    }

    [Fact]
    public async Task TheList_IsNewestFirst_AcrossAPageBoundary_WithTheTotalOfEveryPage()
    {
        var tag = NewTag();
        var oldest = await AcceptedAsync(tag, 1);
        var middle = await AcceptedAsync(tag, 2);
        var newest = await AcceptedAsync(tag, 3);

        var first = await _reader.ListAsync(tag, _cancellationToken, pageSize: 2);
        var second = await _reader.ListAsync(tag, _cancellationToken, page: 2, pageSize: 2);

        Numbers(first).Should().Equal(newest, middle);
        Numbers(second).Should().Equal(oldest);
        first.TotalCount.Should().Be(3);
        second.TotalCount.Should().Be(3);
        first.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task TheList_FilteredByStatus_ShowsOnlyAcceptedOrOnlyPlannedOrders()
    {
        var tag = NewTag();
        var accepted = await AcceptedAsync(tag, 1);
        var (planned, _) = await PlannedAsync(tag, 2, PlanningCalls.ToOwnFleet());

        var onlyAccepted = await _reader.ListAsync(tag, _cancellationToken, TransportOrderStatus.Accepted);
        var onlyPlanned = await _reader.ListAsync(tag, _cancellationToken, TransportOrderStatus.Planned);
        var both = await _reader.ListAsync(tag, _cancellationToken);

        Numbers(onlyAccepted).Should().Equal(accepted);
        Numbers(onlyPlanned).Should().Equal(planned);
        onlyAccepted.TotalCount.Should().Be(1);
        onlyPlanned.TotalCount.Should().Be(1);
        Numbers(both).Should().Equal(planned, accepted);
    }

    [Fact]
    public async Task TheList_FilteredByShipper_ShowsOnlyThatShippersOrders()
    {
        var tag = NewTag();
        var brouwer = await AcceptedAsync(tag, 1);
        var groenrijk = await AcceptedAsync(tag, 2, shipperId: "SHP-0003");

        var ofGroenrijk = await _reader.ListAsync(tag, _cancellationToken, shipperId: "SHP-0003");
        var ofBrouwer = await _reader.ListAsync(tag, _cancellationToken, shipperId: "SHP-0001");

        Numbers(ofGroenrijk).Should().Equal(groenrijk);
        ofGroenrijk.Items.Single().Shipper.Name.Should().Be("Tuincentrum Groenrijk");
        Numbers(ofBrouwer).Should().Equal(brouwer);
        ofBrouwer.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task TheList_NeverShowsARejectedBooking()
    {
        var tag = NewTag();
        var accepted = await AcceptedAsync(tag, 1);
        var booking = Bookings.Valid(Reference(tag, 2));
        booking["shipperId"] = "SHP-0002";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative)) { Content = JsonContent.Create(booking) };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        using var rejection = await tms.Tms.SendAsync(request, _cancellationToken);
        rejection.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertRejectedBookingExistsAsync(Reference(tag, 2));

        var listed = await _reader.ListAsync(tag, _cancellationToken);
        var rejectedShipper = await _reader.ListAsync(tag, _cancellationToken, shipperId: "SHP-0002");

        Numbers(listed).Should().Equal(accepted);
        listed.TotalCount.Should().Be(1);
        rejectedShipper.Items.Should().BeEmpty();
        rejectedShipper.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Search_MatchesATransportOrderNumber_AShipperReference_AndAShipmentNumber_WhateverTheirCase()
    {
        var tag = NewTag();
        var (planned, shipment) = await PlannedAsync(tag, 1, PlanningCalls.ToOwnFleet());
        var other = await AcceptedAsync(tag, 2);

        var byNumber = await _reader.ListAsync(planned.ToLowerInvariant(), _cancellationToken);
        var byReference = await _reader.ListAsync(Reference(tag, 2).ToLowerInvariant(), _cancellationToken);
        var byShipment = await _reader.ListAsync(shipment.ToLowerInvariant(), _cancellationToken);
        var byPartOfReference = await _reader.ListAsync($"  {tag.ToLowerInvariant()[2..6]}  ", _cancellationToken);

        Numbers(byNumber).Should().Equal(planned);
        Numbers(byReference).Should().Equal(other);
        Numbers(byShipment).Should().Equal(planned);
        Numbers(byPartOfReference).Should().Equal(other, planned);
    }

    [Fact]
    public async Task Search_WithoutAMatch_GivesAnEmptyPage_NotAnError()
    {
        var page = await _reader.ListAsync($"NOTHING-{Guid.NewGuid():N}", _cancellationToken);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.PageCount.Should().Be(0);
    }

    [Fact]
    public async Task APlannedOrdersRow_CarriesItsShipmentNumberAndStatus_AnAcceptedOrdersDoesNot()
    {
        var tag = NewTag();
        var accepted = await AcceptedAsync(tag, 1);
        var (planned, shipment) = await PlannedAsync(tag, 2, PlanningCalls.ToOwnFleet());

        var page = await _reader.ListAsync(tag, _cancellationToken);

        var plannedRow = page.Items.Single(row => row.TransportOrderNumber == planned);
        plannedRow.Status.Should().Be(TransportOrderStatus.Planned);
        plannedRow.ShipmentNumber.Should().Be(shipment);
        plannedRow.ShipmentStatus.Should().Be(ShipmentStatus.Planned);
        var acceptedRow = page.Items.Single(row => row.TransportOrderNumber == accepted);
        acceptedRow.Status.Should().Be(TransportOrderStatus.Accepted);
        acceptedRow.ShipmentNumber.Should().BeNull();
        acceptedRow.ShipmentStatus.Should().BeNull();
        acceptedRow.Shipper.Id.Should().Be("SHP-0001");
        acceptedRow.ShipperReference.Should().Be(Reference(tag, 1));
    }

    [Fact]
    public async Task TheDetail_OfAnAcceptedOrder_HasNoShipment()
    {
        var number = await AcceptedAsync(NewTag(), 1);

        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        detail.TransportOrder.TransportOrderNumber.Should().Be(number);
        detail.TransportOrder.Status.Should().Be(TransportOrderStatus.Accepted);
        detail.Shipment.Should().BeNull();
        detail.Trip.Should().BeNull();
    }

    [Fact]
    public async Task TheDetail_OfAPlannedOwnFleetOrderNotYetOnATrip_HasItsShipmentAndNoTrip()
    {
        var (number, shipment) = await PlannedAsync(NewTag(), 1, PlanningCalls.ToOwnFleet());

        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        detail.TransportOrder.Status.Should().Be(TransportOrderStatus.Planned);
        detail.Shipment!.ShipmentNumber.Should().Be(shipment);
        detail.Shipment.Status.Should().Be(ShipmentStatus.Planned);
        detail.Shipment.CarriedBy.AssignedTo.Should().Be(AssignedTo.OwnFleet);
        detail.Shipment.CarriedBy.Carrier.Should().BeNull();
        detail.Shipment.CarrierBooking.Should().BeNull();
        detail.Trip.Should().BeNull();
    }

    [Fact]
    public async Task TheDetail_OfAnOwnFleetOrderOnATrip_HasTheTripWithTheOutcomesOfItsStops()
    {
        var day = await OpenHorizonAsync(3500);
        var (number, shipment) = await ReleasedAsync(NewTag(), 1, PlanningCalls.ToOwnFleet(), day, collectFrom: 8);
        var trip = await StartedTripAsync(day, shipment);
        var collected = ShortlyAfterMidnightOf(day).AddMinutes(-30);
        using var report = await tms.RecordAsync(tms.DriverReports, StopReport("Collected", trip, shipment, collected), _cancellationToken);
        report.StatusCode.Should().Be(HttpStatusCode.OK, await report.Content.ReadAsStringAsync(_cancellationToken));

        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        detail.Shipment!.ShipmentNumber.Should().Be(shipment);
        detail.Shipment.Status.Should().Be(ShipmentStatus.Collected);
        var onTrip = detail.Trip!;
        onTrip.TripNumber.Should().Be(trip);
        onTrip.Day.Should().Be(day);
        onTrip.Status.Should().Be(TripStatus.InProgress);
        onTrip.Stops.Select(stop => (stop.Kind, stop.ShipmentNumber, stop.Outcome)).Should().Equal(
            (StopKind.Collection, shipment, StopOutcomeKind.Collected),
            (StopKind.Delivery, shipment, StopOutcomeKind.Open));
        onTrip.Stops[0].OutcomeRemark.Should().Be("Collected at the Stop");
    }

    [Fact]
    public async Task TheDetail_OfACarrierOrder_HasItsCarrierBooking()
    {
        var (number, shipment) = await PlannedAsync(NewTag(), 1, PlanningCalls.ToCarrier());
        var released = await tms.ReleasedAsync(shipment, _cancellationToken);

        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        detail.Shipment!.Status.Should().Be(ShipmentStatus.Released);
        detail.Shipment.CarriedBy.AssignedTo.Should().Be(AssignedTo.Carrier);
        detail.Shipment.CarriedBy.Carrier!.Id.Should().Be(PlanningCalls.CarrierId);
        var booking = detail.Shipment.CarrierBooking!;
        booking.CarrierBookingNumber.Should().Be(released["carrierBookingNumber"]!.GetValue<string>());
        booking.Status.Should().Be(CarrierBookingStatus.Requested);
        booking.Carrier.Id.Should().Be(PlanningCalls.CarrierId);
        detail.Trip.Should().BeNull();
    }

    [Fact]
    public async Task TheDetail_OfAnUnknownNumber_IsNotFound()
    {
        var result = await _reader.DetailAsync("TO-9999999", _cancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
    }

    [Fact]
    public async Task AShipmentReassignedFromOwnFleetToACarrierAndBack_ShowsOnce_WithItsCurrentCarrier()
    {
        var tag = NewTag();
        var (number, shipment) = await PlannedAsync(tag, 1, PlanningCalls.ToOwnFleet());
        await tms.ReassignedAsync(shipment, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.ReassignedAsync(shipment, PlanningCalls.ToOwnFleet(), _cancellationToken);

        var page = await _reader.ListAsync(tag, _cancellationToken);
        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        page.Items.Should().ContainSingle().Which.ShipmentNumber.Should().Be(shipment);
        page.TotalCount.Should().Be(1);
        detail.Shipment!.ShipmentNumber.Should().Be(shipment);
        detail.Shipment.CarriedBy.AssignedTo.Should().Be(AssignedTo.OwnFleet);
        detail.Shipment.CarrierBooking.Should().BeNull();
    }

    [Fact]
    public async Task AShipmentReassignedFromOwnFleetToACarrierAndReleased_ShowsOnce_WithItsCarrierAndBookingStatus()
    {
        var tag = NewTag();
        var (number, shipment) = await PlannedAsync(tag, 1, PlanningCalls.ToOwnFleet());
        await tms.ReassignedAsync(shipment, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.ReleasedAsync(shipment, _cancellationToken);

        var page = await _reader.ListAsync(tag, _cancellationToken);
        var detail = (await _reader.DetailAsync(number, _cancellationToken)).Value;

        page.Items.Should().ContainSingle().Which.ShipmentStatus.Should().Be(ShipmentStatus.Released);
        detail.Shipment!.CarriedBy.Carrier!.Id.Should().Be(PlanningCalls.CarrierId);
        detail.Shipment.CarrierBooking!.Carrier.Id.Should().Be(PlanningCalls.CarrierId);
        detail.Shipment.CarrierBooking.Status.Should().Be(CarrierBookingStatus.Requested);
    }

    private static string NewTag() => $"BR{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    private static string Reference(string tag, int index) => $"{tag}-{index}";

    private static string[] Numbers(TransportOrderPage page) => [.. page.Items.Select(item => item.TransportOrderNumber)];

    private async Task<string> AcceptedAsync(string tag, int index, string shipperId = "SHP-0001", DateTimeOffset? collectFrom = null)
    {
        var booking = Bookings.Valid(Reference(tag, index));
        booking["shipperId"] = shipperId;
        if (collectFrom is { } from)
        {
            booking = booking.CollectedFrom(from);
        }

        return await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
    }

    private async Task<(string Number, string Shipment)> PlannedAsync(string tag, int index, JsonObject assignment, DateTimeOffset? collectFrom = null)
    {
        var number = await AcceptedAsync(tag, index, collectFrom: collectFrom);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, assignment, _cancellationToken);
        return (number, assigned["shipmentNumber"]!.GetValue<string>());
    }

    // An Own fleet Shipment the Planner released, collecting from the hour on the day.
    private async Task<(string Number, string Shipment)> ReleasedAsync(string tag, int index, JsonObject assignment, DateOnly day, int collectFrom)
    {
        var local = day.ToDateTime(new TimeOnly(collectFrom, 0));
        var (number, shipment) = await PlannedAsync(tag, index, assignment, new DateTimeOffset(local, Amsterdam.GetUtcOffset(local)));
        await tms.ReleasedAsync(shipment, _cancellationToken);
        return (number, shipment);
    }

    private async Task AssertRejectedBookingExistsAsync(string reference)
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/transport-orders?status=Rejected", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonNode.Parse(body)!.AsArray().Should().Contain(order => order!["shipperReference"]!.GetValue<string>() == reference);
    }

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // Puts the Shipment on a Trip for the day, dispatches it, and has the Driver start it.
    private async Task<string> StartedTripAsync(DateOnly day, string shipment)
    {
        using var created = await PostTmsUiAsync("/v1/trips", new
        {
            day = $"{day:yyyy-MM-dd}",
            vehicleId = Truck,
            driverId = Joost,
            plannedStart = "09:00",
            plannedDrivingDuration = "04:00:00",
            shipmentNumbers = new[] { shipment },
        });
        var createdBody = await created.Content.ReadAsStringAsync(_cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
        var trip = JsonNode.Parse(createdBody)!["number"]!.GetValue<string>();

        using var dispatched = await PostTmsUiAsync($"/v1/trips/{trip}/dispatch", null);
        var dispatchedBody = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, dispatchedBody);
        await tms.PublishedAsync(JsonNode.Parse(dispatchedBody)!["correlationId"]!.GetValue<string>(), "TripDispatched", _cancellationToken);

        tms.SetClock(ShortlyAfterMidnightOf(day));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/start", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = Joost }),
        };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        using var started = await tms.TripStart.SendAsync(request, _cancellationToken);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(_cancellationToken));
        return trip;
    }

    private async Task<HttpResponseMessage> PostTmsUiAsync(string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, _cancellationToken);
    }

    private static JsonObject StopReport(string type, string trip, string shipment, DateTimeOffset eventTime)
    {
        var report = TrackingEventCalls.Event(type, "unused", eventTime, source: new JsonObject { ["type"] = "OwnFleet", ["driverId"] = Joost });
        report.Remove("carrierBookingReference");
        report["tripNumber"] = trip;
        report["shipmentNumber"] = shipment;
        report["tripVersion"] = 1;
        report["location"] = new JsonObject { ["place"] = "Utrecht", ["postcode"] = "3511 AW", ["countryCode"] = "NL" };
        report["remark"] = $"{type} at the Stop";
        return report;
    }
}
