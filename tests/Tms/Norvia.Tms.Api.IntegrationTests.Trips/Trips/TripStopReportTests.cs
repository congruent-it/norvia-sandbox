using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Capacity.CapacityCalls;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// A Driver's Stop reports (P12) at the TMS's boundary: the Driver reports Logic App records them as Tracking events from Own fleet
/// under the role TrackingEvents.Record. Collected and Delivered move the Shipment Dispatched, Collected, Delivered and give the Stop its outcome,
/// the failures and damage open their Exception and move nothing, the Driver report rules refuse the rest, and a repeated Driver app event ID
/// publishes nothing. A Driver's Proof of delivery is recorded from Own fleet through the Proof of delivery API under the role ProofsOfDelivery.Record
/// by the same Logic App, under the same Driver report rules, on the correlation ID of its Delivered event. The clock is controlled, so each test sets the day it works on.
/// </summary>
public sealed class TripStopReportTests(TripsTmsFixture tms) : IClassFixture<TripsTmsFixture>, IDisposable
{
    private const string Truck = "VEH-0002";
    private const string Joost = "DRV-0002";
    private const string Anouk = "DRV-0001";

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateOnly _realToday = Today(DateTimeOffset.UtcNow);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task Collected_ThenDelivered_MoveTheShipment_GiveTheStopsTheirOutcomes_PublishTheirEvents_AndShowOnThePlannersViews()
    {
        var (trip, day, shipment) = await StartedTripAsync(6500);
        var now = ShortlyAfterMidnightOf(day);
        var collectedTrail = TmsCalls.NewCorrelationId();

        var collected = await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-30), remark: "Dock 4"), _cancellationToken, collectedTrail);

        collected["shipmentStatus"]!.GetValue<string>().Should().Be("Collected");
        collected["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        collected["correlationId"]!.GetValue<string>().Should().Be(collectedTrail);
        (await ShipmentStatusAsync(shipment)).Should().Be("Collected");

        var message = (await tms.PublishedAsync(collectedTrail, "ShipmentCollected", _cancellationToken)).Should().ContainSingle().Subject;
        message.ApplicationProperties["tripNumber"].Should().Be(trip);
        message.ApplicationProperties["flow"].Should().Be("trip-execution");
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentCollected(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["carrierBookingReference"].Should().BeNull();
        body["carrierId"].Should().BeNull();
        body["correlationId"]!.GetValue<string>().Should().Be(collectedTrail);

        var inProgress = (await tms.ListShipmentsInProgressAsync(_cancellationToken)).Single(item => item!["shipmentNumber"]!.GetValue<string>() == shipment)!;
        inProgress["tripNumber"]!.GetValue<string>().Should().Be(trip);
        inProgress["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("OwnFleet");

        var afterCollection = await GetTripAsync(trip);
        var collectionStop = afterCollection["stops"]!.AsArray()[0]!;
        collectionStop["outcome"]!.GetValue<string>().Should().Be("Collected");
        collectionStop["outcomeRemark"]!.GetValue<string>().Should().Be("Dock 4");
        DateTimeOffset.Parse(collectionStop["outcomeAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(now.AddMinutes(-30));
        afterCollection["stops"]!.AsArray()[1]!["outcome"]!.GetValue<string>().Should().Be("Open");
        (await ListedAsync(trip))["stopsDone"]!.GetValue<int>().Should().Be(1);

        await AssertRefusedAsync(await RecordAsync(Report("Collected", trip, shipment, now.AddMinutes(-20))), HttpStatusCode.Conflict, "StopAlreadyCompleted");
        await AssertRefusedAsync(await RecordAsync(Report("CollectionFailed", trip, shipment, now.AddMinutes(-20))), HttpStatusCode.Conflict, "StopAlreadyCompleted");

        var deliveredTrail = TmsCalls.NewCorrelationId();
        var delivered = await tms.RecordedAsync(Report("Delivered", trip, shipment, now.AddMinutes(-10)), _cancellationToken, deliveredTrail);

        delivered["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        var deliveredMessage = (await tms.PublishedAsync(deliveredTrail, "ShipmentDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        deliveredMessage.ApplicationProperties["tripNumber"].Should().Be(trip);
        var deliveredBody = JsonNode.Parse(deliveredMessage.Body.ToString())!;
        var deliveredResults = CanonicalModelSchemas.ValidateShipmentDelivered(deliveredBody);
        deliveredResults.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(deliveredResults));
        (await GetTripAsync(trip))["stops"]!.AsArray()[1]!["outcome"]!.GetValue<string>().Should().Be("Delivered");
        (await ListedAsync(trip))["stopsDone"]!.GetValue<int>().Should().Be(2);
        await AssertRefusedAsync(await RecordAsync(Report("DeliveryFailed", trip, shipment, now.AddMinutes(-5))), HttpStatusCode.Conflict, "StopAlreadyCompleted");
    }

    [Fact]
    public async Task ADeliveryBeforeItsCollection_IsCollectionBeforeDelivery_AlsoAfterACollectionThatFailed()
    {
        var (trip, day, shipment) = await StartedTripAsync(6600);
        var now = ShortlyAfterMidnightOf(day);

        await AssertRefusedAsync(await RecordAsync(Report("Delivered", trip, shipment, now.AddMinutes(-5))), HttpStatusCode.Conflict, "CollectionBeforeDelivery");
        await AssertRefusedAsync(await RecordAsync(Report("DeliveryFailed", trip, shipment, now.AddMinutes(-5))), HttpStatusCode.Conflict, "CollectionBeforeDelivery");
        (await tms.RecordedAsync(Report("CollectionFailed", trip, shipment, now.AddMinutes(-4)), _cancellationToken))["movedShipment"]!.GetValue<bool>().Should().BeFalse();
        await AssertRefusedAsync(await RecordAsync(Report("Delivered", trip, shipment, now.AddMinutes(-3))), HttpStatusCode.Conflict, "CollectionBeforeDelivery");
        (await ShipmentStatusAsync(shipment)).Should().Be("Dispatched");
    }

    [Theory]
    [InlineData("CollectionFailed", "Dispatched")]
    [InlineData("Damaged", "Dispatched")]
    public async Task CollectionFailedAndDamage_ChangeNoStatus_AndOpenTheMatchingException_OnTheReportsTrail(string type, string status)
    {
        var (trip, day, shipment) = await StartedTripAsync(type == "Damaged" ? 6700 : 6750);
        var now = ShortlyAfterMidnightOf(day);
        var trail = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Report(type, trip, shipment, now.AddMinutes(-5), remark: "Pallet crushed"), _cancellationToken, trail);

        recorded["movedShipment"]!.GetValue<bool>().Should().BeFalse();
        (await ShipmentStatusAsync(shipment)).Should().Be(status);
        var reported = (await tms.PublishedAsync(trail, "ShipmentExceptionReported", _cancellationToken)).Should().ContainSingle().Subject;
        var reportedBody = JsonNode.Parse(reported.Body.ToString())!;
        var reportedResults = CanonicalModelSchemas.ValidateShipmentExceptionReported(reportedBody);
        reportedResults.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(reportedResults));
        reportedBody["tripNumber"]!.GetValue<string>().Should().Be(trip);
        var opened = (await tms.PublishedAsync(trail, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject;
        var openedBody = JsonNode.Parse(opened.Body.ToString())!;
        var openedResults = CanonicalModelSchemas.ValidateExceptionOpened(openedBody);
        openedResults.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(openedResults));
        openedBody["exceptionType"]!.GetValue<string>().Should().Be(type);
        openedBody["shipmentNumber"]!.GetValue<string>().Should().Be(shipment);
        openedBody["carrierId"].Should().BeNull();
    }

    [Fact]
    public async Task ReportDamage_IsNoOutcome_SoTheStopIsStillOpen_AndItIsAllowedAfterTheStopIsCompleted()
    {
        var (trip, day, shipment) = await StartedTripAsync(6800);
        var now = ShortlyAfterMidnightOf(day);

        await tms.RecordedAsync(Report("Damaged", trip, shipment, now.AddMinutes(-9)), _cancellationToken);
        (await GetTripAsync(trip))["stops"]!.AsArray()[0]!["outcome"]!.GetValue<string>().Should().Be("Open");
        (await ListedAsync(trip))["stopsDone"]!.GetValue<int>().Should().Be(0);

        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-8)), _cancellationToken);
        await tms.RecordedAsync(Report("Damaged", trip, shipment, now.AddMinutes(-7)), _cancellationToken);
        await tms.RecordedAsync(Report("Delivered", trip, shipment, now.AddMinutes(-6)), _cancellationToken);
        await tms.RecordedAsync(Report("Damaged", trip, shipment, now.AddMinutes(-5)), _cancellationToken);
        (await ShipmentStatusAsync(shipment)).Should().Be("Delivered");
    }

    [Fact]
    public async Task DeliveryFailed_AfterTheCollection_ChangesNoStatus_AndOpensAnException_AndTheStopHasThatOutcome()
    {
        var (trip, day, shipment) = await StartedTripAsync(6900);
        var now = ShortlyAfterMidnightOf(day);
        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-9)), _cancellationToken);
        var trail = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(Report("DeliveryFailed", trip, shipment, now.AddMinutes(-5), remark: "Nobody home"), _cancellationToken, trail);

        (await ShipmentStatusAsync(shipment)).Should().Be("Collected");
        var opened = (await tms.PublishedAsync(trail, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject;
        JsonNode.Parse(opened.Body.ToString())!["exceptionType"]!.GetValue<string>().Should().Be("DeliveryFailed");
        var stop = (await GetTripAsync(trip))["stops"]!.AsArray()[1]!;
        stop["outcome"]!.GetValue<string>().Should().Be("DeliveryFailed");
        stop["outcomeRemark"]!.GetValue<string>().Should().Be("Nobody home");
    }

    [Fact]
    public async Task AShipmentTakenOffTheTrip_IsShipmentNotOnTrip_EvenWhenTheReportNamesTheOlderVersion_AndTheShipmentStaysPlanned()
    {
        var (trip, day, shipment) = await StartedTripAsync(7000);
        var now = ShortlyAfterMidnightOf(day);
        using var tookOff = await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{trip}/shipments/{shipment}/take-off", UriKind.Relative), new { reason = "Customer cancelled" }, _cancellationToken);
        tookOff.StatusCode.Should().Be(HttpStatusCode.OK, await tookOff.Content.ReadAsStringAsync(_cancellationToken));

        await AssertRefusedAsync(await RecordAsync(Report("Collected", trip, shipment, now.AddMinutes(-5), version: 1)), HttpStatusCode.Conflict, "ShipmentNotOnTrip");
        await AssertRefusedAsync(await RecordAsync(Report("Collected", trip, shipment, now.AddMinutes(-5), version: 2)), HttpStatusCode.Conflict, "ShipmentNotOnTrip");
        (await ShipmentStatusAsync(shipment)).Should().Be("Planned");
    }

    [Fact]
    public async Task ARepeatedDriverAppEventId_ReturnsTheOriginalResultFlaggedAsARepeat_AndPublishesNothing()
    {
        var (trip, day, shipment) = await StartedTripAsync(7100);
        var now = ShortlyAfterMidnightOf(day);
        var report = Report("Collected", trip, shipment, now.AddMinutes(-5));
        var firstTrail = TmsCalls.NewCorrelationId();
        var first = await tms.RecordedAsync(report, _cancellationToken, firstTrail);
        await tms.PublishedAsync(firstTrail, "ShipmentCollected", _cancellationToken);

        var secondTrail = TmsCalls.NewCorrelationId();
        var repeat = await tms.RecordedAsync(report, _cancellationToken, secondTrail);

        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeat["correlationId"]!.GetValue<string>().Should().Be(firstTrail, "a repeat answers on the trail of the report it repeats");
        repeat["shipmentStatus"]!.GetValue<string>().Should().Be(first["shipmentStatus"]!.GetValue<string>());
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == secondTrail).Should().BeEmpty();
        tms.Published.Count(m => m.Subject == "ShipmentCollected" && m.CorrelationId == firstTrail).Should().Be(1);
    }

    [Fact]
    public async Task AnotherDriversTrip_AnUnknownTrip_AndATripNotUnderWay_AreRefused_AndAFutureEventTimeIsABadRequest()
    {
        var (trip, day, shipment) = await StartedTripAsync(7200);
        var now = ShortlyAfterMidnightOf(day);
        var notStarted = await DispatchedTripAsync(7250);
        tms.SetClock(ShortlyAfterMidnightOf(day));

        await AssertRefusedAsync(await RecordAsync(Report("Collected", trip, shipment, now.AddMinutes(-5), driver: Anouk)), HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(await RecordAsync(Report("Collected", "TR-9999999", shipment, now.AddMinutes(-5))), HttpStatusCode.NotFound, "TripNotFound");
        await AssertRefusedAsync(await RecordAsync(Report("Collected", notStarted.Trip, notStarted.Shipment, now.AddMinutes(-5))), HttpStatusCode.Conflict, "TripNotUnderWay");
        await AssertRefusedAsync(await RecordAsync(Report("Collected", trip, shipment, now.AddHours(1))), HttpStatusCode.BadRequest, "EventTimeInTheFuture");
        await AssertRefusedAsync(await RecordAsync(Report("Departed", trip, shipment, now.AddMinutes(-5))), HttpStatusCode.BadRequest, "UnknownEventType");
        (await ShipmentStatusAsync(shipment)).Should().Be("Dispatched");
        (await ShipmentStatusAsync(notStarted.Shipment)).Should().Be("Dispatched");
    }

    [Fact]
    public async Task AnOwnFleetReport_NeedsItsTripShipmentVersionAndDriver_NamesNoCarrierBooking_AndOnlyTheDriverReportsLogicAppMayRecordIt()
    {
        var (trip, day, shipment) = await StartedTripAsync(7300);
        var now = ShortlyAfterMidnightOf(day);
        var valid = Report("Collected", trip, shipment, now.AddMinutes(-5));
        CanonicalModelSchemas.ValidateTrackingEvent(valid).IsValid.Should().BeTrue();

        foreach (var (without, send) in new (string, Action<JsonObject>)[]
        {
            ("tripNumber", report => report.Remove("tripNumber")),
            ("shipmentNumber", report => report.Remove("shipmentNumber")),
            ("tripVersion", report => report.Remove("tripVersion")),
            ("driverId", report => report["source"]!.AsObject().Remove("driverId")),
        })
        {
            var broken = (JsonObject)valid.DeepClone();
            broken["eventId"] = Guid.NewGuid().ToString();
            send(broken);
            using var response = await RecordAsync(broken);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"without {without}");
        }

        var withBooking = (JsonObject)valid.DeepClone();
        withBooking["carrierBookingReference"] = "VDT-1";
        (await RecordAsync(withBooking)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        foreach (var caller in new[] { tms.TmsUi, tms.Tms, tms.TripDeliveryReport, tms.TripStart })
        {
            (await tms.RecordAsync(caller, (JsonObject)valid.DeepClone(), _cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await ShipmentStatusAsync(shipment)).Should().Be("Dispatched");
    }

    [Fact]
    public async Task ACleanProofOfDelivery_IsCurrent_AndMakesTheDeliveredShipmentsOrderBillable_OnTheTrailOfItsDeliveredEvent()
    {
        var (trip, day, shipment) = await StartedTripAsync(7400);
        var now = ShortlyAfterMidnightOf(day);
        var order = (await tms.GetShipmentAsync(shipment, _cancellationToken))["transportOrderNumber"]!.GetValue<string>();
        var trail = TmsCalls.NewCorrelationId();
        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-30)), _cancellationToken);
        await tms.RecordedAsync(Report("Delivered", trip, shipment, now.AddMinutes(-10)), _cancellationToken, trail);
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable", "a delivery alone doesn't make an order Billable");
        var metadata = ProofReport(trip, shipment, now.AddMinutes(-10));
        var results = CanonicalModelSchemas.ValidateProofOfDelivery(metadata);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));

        var recorded = await tms.RecordedProofAsync(tms.DriverReports, metadata, [Signature()], _cancellationToken, trail);

        recorded["shipmentNumber"]!.GetValue<string>().Should().Be(shipment);
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        recorded["correlationId"]!.GetValue<string>().Should().Be(trail);
        using var current = await tms.GetProofAsync(shipment, _cancellationToken);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        var proof = JsonNode.Parse(await current.Content.ReadAsStringAsync(_cancellationToken))!;
        proof["source"]!.GetValue<string>().Should().Be("Own fleet");
        proof["receivedBy"]!.GetValue<string>().Should().Be("A. de Vries");
        proof["files"]!.AsArray().Should().ContainSingle();

        var received = (await tms.PublishedAsync(trail, "ProofOfDeliveryReceived", _cancellationToken)).Should().ContainSingle().Subject;
        received.ApplicationProperties["tripNumber"].Should().Be(trip);
        var body = JsonNode.Parse(received.Body.ToString())!;
        var receivedResults = CanonicalModelSchemas.ValidateProofOfDeliveryReceived(body);
        receivedResults.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(receivedResults));
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["carrierId"].Should().BeNull();
        body["carrierBookingReference"].Should().BeNull();
        await tms.WaitForBillableAsync(order, _cancellationToken);
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");
        tms.BillablePublished(order).Should().ContainSingle().Which.CorrelationId.Should().Be(trail);
    }

    [Fact]
    public async Task AProofOfDeliveryWithReservations_OpensAReservationsException_AndLeavesTheOrderNotYetBillable()
    {
        var (trip, day, shipment) = await StartedTripAsync(7500);
        var now = ShortlyAfterMidnightOf(day);
        var order = (await tms.GetShipmentAsync(shipment, _cancellationToken))["transportOrderNumber"]!.GetValue<string>();
        var trail = TmsCalls.NewCorrelationId();
        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-30)), _cancellationToken);
        await tms.RecordedAsync(Report("Delivered", trip, shipment, now.AddMinutes(-10)), _cancellationToken, trail);
        var metadata = ProofReport(trip, shipment, now.AddMinutes(-10));
        metadata["condition"] = "Reservations";
        metadata["remark"] = "Two pallets wet";

        await tms.RecordedProofAsync(tms.DriverReports, metadata, [Signature()], _cancellationToken, trail);

        var opened = (await tms.PublishedAsync(trail, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject;
        var openedBody = JsonNode.Parse(opened.Body.ToString())!;
        var openedResults = CanonicalModelSchemas.ValidateExceptionOpened(openedBody);
        openedResults.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(openedResults));
        openedBody["exceptionType"]!.GetValue<string>().Should().Be("Reservations");
        openedBody["shipmentNumber"]!.GetValue<string>().Should().Be(shipment);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(order).Should().BeEmpty();
        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("NotYetBillable");
    }

    [Fact]
    public async Task AProofOfDelivery_FollowsTheDriverReportRules_AndARepeatedDocumentIdPublishesNothing()
    {
        var (trip, day, shipment) = await StartedTripAsync(7600);
        var now = ShortlyAfterMidnightOf(day);
        var notStarted = await DispatchedTripAsync(7650);
        tms.SetClock(ShortlyAfterMidnightOf(day));

        await AssertProofRefusedAsync(ProofReport(trip, shipment, now.AddMinutes(-5)), HttpStatusCode.Conflict, "CollectionBeforeDelivery");
        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-20)), _cancellationToken);
        await AssertProofRefusedAsync(ProofReport(trip, shipment, now.AddMinutes(-5), driver: Anouk), HttpStatusCode.NotFound, "TripNotFound");
        await AssertProofRefusedAsync(ProofReport("TR-9999999", shipment, now.AddMinutes(-5)), HttpStatusCode.NotFound, "TripNotFound");
        await AssertProofRefusedAsync(ProofReport(notStarted.Trip, notStarted.Shipment, now.AddMinutes(-5)), HttpStatusCode.Conflict, "TripNotUnderWay");
        await AssertProofRefusedAsync(ProofReport(trip, notStarted.Shipment, now.AddMinutes(-5)), HttpStatusCode.Conflict, "ShipmentNotOnTrip");
        await AssertProofRefusedAsync(ProofReport(trip, shipment, now.AddHours(1)), HttpStatusCode.BadRequest, "DeliveryTimeInTheFuture");
        using var none = await tms.GetProofAsync(shipment, _cancellationToken);
        none.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (takenOffTrip, takenOffDay, takenOffShipment) = await StartedTripAsync(7620);
        using var tookOff = await tms.TmsUi.PostAsJsonAsync(new Uri($"/v1/trips/{takenOffTrip}/shipments/{takenOffShipment}/take-off", UriKind.Relative), new { reason = "Customer cancelled" }, _cancellationToken);
        tookOff.StatusCode.Should().Be(HttpStatusCode.OK, await tookOff.Content.ReadAsStringAsync(_cancellationToken));
        await AssertProofRefusedAsync(ProofReport(takenOffTrip, takenOffShipment, ShortlyAfterMidnightOf(takenOffDay).AddMinutes(-5), version: 1), HttpStatusCode.Conflict, "ShipmentNotOnTrip");

        var (otherTrip, otherDay, otherShipment) = await StartedTripAsync(7700);
        var otherNow = ShortlyAfterMidnightOf(otherDay);
        await tms.RecordedAsync(Report("Collected", otherTrip, otherShipment, otherNow.AddMinutes(-20)), _cancellationToken);
        var metadata = ProofReport(otherTrip, otherShipment, otherNow.AddMinutes(-5));
        var firstTrail = TmsCalls.NewCorrelationId();
        var first = await tms.RecordedProofAsync(tms.DriverReports, metadata, [Signature()], _cancellationToken, firstTrail);
        await tms.PublishedAsync(firstTrail, "ProofOfDeliveryReceived", _cancellationToken);

        var secondTrail = TmsCalls.NewCorrelationId();
        var repeat = await tms.RecordedProofAsync(tms.DriverReports, metadata, [Signature()], _cancellationToken, secondTrail);

        repeat["isRepeat"]!.GetValue<bool>().Should().BeTrue();
        repeat["proofOfDeliveryId"]!.GetValue<string>().Should().Be(first["proofOfDeliveryId"]!.GetValue<string>());
        repeat["correlationId"]!.GetValue<string>().Should().Be(firstTrail);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == secondTrail).Should().BeEmpty();
    }

    [Fact]
    public async Task AnOwnFleetProofOfDelivery_NeedsItsTripShipmentVersionAndDriver_NamesNoCarrierBooking_AndOnlyTheRecordRoleMayRecordIt()
    {
        var (trip, day, shipment) = await StartedTripAsync(7800);
        var now = ShortlyAfterMidnightOf(day);
        await tms.RecordedAsync(Report("Collected", trip, shipment, now.AddMinutes(-20)), _cancellationToken);
        var valid = ProofReport(trip, shipment, now.AddMinutes(-5));

        foreach (var (without, send) in new (string, Action<JsonObject>)[]
        {
            ("tripNumber", report => report.Remove("tripNumber")),
            ("shipmentNumber", report => report.Remove("shipmentNumber")),
            ("tripVersion", report => report.Remove("tripVersion")),
            ("driverId", report => report["source"]!.AsObject().Remove("driverId")),
        })
        {
            var broken = (JsonObject)valid.DeepClone();
            broken["documentId"] = Guid.NewGuid().ToString();
            send(broken);
            var problem = await tms.RecordProofRefusedAsync(tms.DriverReports, broken, [Signature()], HttpStatusCode.BadRequest, _cancellationToken);
            problem["code"]!.GetValue<string>().Should().Be("MissingRequiredField", $"without {without}");
        }

        var withBooking = (JsonObject)valid.DeepClone();
        withBooking["carrierBookingReference"] = "VDT-1";
        await tms.RecordProofRefusedAsync(tms.DriverReports, withBooking, [Signature()], HttpStatusCode.BadRequest, _cancellationToken);

        foreach (var caller in new[] { tms.TmsUi, tms.Tms, tms.TripDeliveryReport, tms.TripStart, tms.TrackingRecord })
        {
            using var response = await tms.RecordProofAsync(caller, (JsonObject)valid.DeepClone(), [Signature()], _cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    private async Task AssertProofRefusedAsync(JsonObject metadata, HttpStatusCode status, string code)
    {
        var problem = await tms.RecordProofRefusedAsync(tms.DriverReports, metadata, [Signature()], status, _cancellationToken);
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }

    private static ProofOfDeliveryCalls.SentFile Signature() =>
        new("signature.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Guid.NewGuid().ToByteArray()]);

    private static JsonObject ProofReport(string trip, string shipment, DateTimeOffset deliveryTime, string driver = Joost, int version = 1) =>
        new()
        {
            ["documentId"] = Guid.NewGuid().ToString(),
            ["source"] = new JsonObject { ["type"] = "OwnFleet", ["driverId"] = driver },
            ["tripNumber"] = trip,
            ["shipmentNumber"] = shipment,
            ["tripVersion"] = version,
            ["receivedBy"] = "A. de Vries",
            ["deliveryTime"] = JsonValue.Create(deliveryTime),
            ["location"] = new JsonObject { ["place"] = "Utrecht", ["postcode"] = "3511 AW", ["countryCode"] = "NL" },
            ["condition"] = "Clean",
            ["files"] = new JsonArray(new JsonObject { ["name"] = "signature.png", ["contentType"] = "image/png" }),
        };

    private Task<HttpResponseMessage> RecordAsync(JsonObject report) => tms.RecordAsync(tms.DriverReports, report, _cancellationToken);

    private static JsonObject Report(string type, string trip, string shipment, DateTimeOffset eventTime, string driver = Joost, int version = 1, string? remark = null)
    {
        var report = TrackingEventCalls.Event(type, "unused", eventTime, source: new JsonObject { ["type"] = "OwnFleet", ["driverId"] = driver });
        report.Remove("carrierBookingReference");
        report["tripNumber"] = trip;
        report["shipmentNumber"] = shipment;
        report["tripVersion"] = version;
        report["location"] = new JsonObject { ["place"] = "Utrecht", ["postcode"] = "3511 AW", ["countryCode"] = "NL" };
        report["remark"] = remark ?? $"{type} at the Stop";
        return report;
    }

    private async Task<(string Trip, DateOnly Day, string Shipment)> StartedTripAsync(int daysAhead)
    {
        var (trip, day, shipment) = await DispatchedTripAsync(daysAhead);
        tms.SetClock(ShortlyAfterMidnightOf(day));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/trips/{trip}/start", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["driverId"] = Joost }),
        };
        request.Headers.Add("X-Correlation-ID", TmsCalls.NewCorrelationId());
        using var started = await tms.TripStart.SendAsync(request, _cancellationToken);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(_cancellationToken));
        return (trip, day, shipment);
    }

    private async Task<(string Trip, DateOnly Day, string Shipment)> DispatchedTripAsync(int daysAhead)
    {
        var day = (await OpenHorizonAsync(daysAhead)).AddDays(1);
        var shipment = await ReleasedAsync(day);
        var trip = await NumberOf(await CreateAsync(day, Truck, Joost, "09:00", "04:00:00", shipment));
        using var dispatched = await tms.TmsUi.PostAsync(new Uri($"/v1/trips/{trip}/dispatch", UriKind.Relative), content: null, _cancellationToken);
        var body = await dispatched.Content.ReadAsStringAsync(_cancellationToken);
        dispatched.StatusCode.Should().Be(HttpStatusCode.OK, body);
        await tms.PublishedAsync(JsonNode.Parse(body)!["correlationId"]!.GetValue<string>(), "TripDispatched", _cancellationToken);
        return (trip, day, shipment);
    }

    private async Task<string> ShipmentStatusAsync(string shipment)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/shipments/{shipment}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["status"]!.GetValue<string>();
    }

    private async Task<JsonNode> GetTripAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri($"/v1/trips/{number}", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private async Task<JsonNode> ListedAsync(string number)
    {
        using var response = await tms.TmsUi.GetAsync(new Uri("/v1/trips", UriKind.Relative), _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray().Single(trip => trip!["number"]!.GetValue<string>() == number)!;
    }

    private async Task<DateOnly> OpenHorizonAsync(int daysAhead)
    {
        var first = _realToday.AddDays(daysAhead);
        first = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
        tms.SetClock(ShortlyAfterMidnightOf(first));
        await tms.HorizonStartingAtAsync(first, _cancellationToken);
        return first;
    }

    // A Released Own fleet Shipment that collects from 08:00 on the day, 2 pallets of 500 kg.
    private async Task<string> ReleasedAsync(DateOnly day)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(8, 0))));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 500m, ["dimensionsCm"] = null } };
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken);
        var shipment = assigned["shipmentNumber"]!.GetValue<string>();
        await tms.ReleasedAsync(shipment, _cancellationToken);
        return shipment;
    }

    private async Task<HttpResponseMessage> CreateAsync(DateOnly day, string? vehicleId, string? driverId, string? start, string? duration, params string[] shipments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                day = $"{day:yyyy-MM-dd}",
                vehicleId,
                driverId,
                plannedStart = start,
                plannedDrivingDuration = duration,
                shipmentNumbers = shipments.Length == 0 ? null : shipments,
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, _cancellationToken);
    }

    private async Task<string> NumberOf(HttpResponseMessage created)
    {
        var body = await created.Content.ReadAsStringAsync(_cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["number"]!.GetValue<string>();
    }

    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(status, body);
        var problem = JsonNode.Parse(body)!.AsObject();
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the refusal has a readable message");
    }
}
