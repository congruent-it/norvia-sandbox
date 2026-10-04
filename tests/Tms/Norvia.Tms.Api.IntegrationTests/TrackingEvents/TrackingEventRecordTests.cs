using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.TrackingEvents;

/// <summary>
/// A Carrier's Tracking event is recorded through the TMS's Tracking event API, as the Carrier-tracking Logic App calls
/// it (P5), and a Collected event moves a Booked Shipment to Collected. The TMS runs with a clock the test sets, so what
/// the TMS says about time (when it recorded the event, whether an event time is in the future) is the test's to decide.
/// </summary>
public sealed class TrackingEventRecordTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    // One second precision, so a time the TMS stores and returns compares equal to the one the test set.
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task Record_CollectedForABookedShipment_MovesItToCollected_AndPublishesOneShipmentCollected()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        var eventTime = new DateTimeOffset(_now.UtcDateTime.AddHours(-1)).ToOffset(TimeSpan.FromHours(2));
        var eta = _now.AddDays(1);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Collected(reference, eventTime, eventId: "evt-1", eta: eta), _cancellationToken, correlationId);

        recorded["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Collected");
        recorded["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        recorded["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Collected");

        var message = (await tms.PublishedAsync(correlationId, "ShipmentCollected", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentCollected(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().MatchRegex("^TO-[0-9]{7}$");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        var published = DateTimeOffset.Parse(body["eventTime"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        published.Should().Be(eventTime);
        published.Offset.Should().Be(TimeSpan.FromHours(2), "the event time keeps the offset of where it happened");
        body["location"]!["place"]!.GetValue<string>().Should().Be("Rotterdam");
        DateTimeOffset.Parse(body["eta"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().Be(eta);
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        DateTimeOffset.Parse(body["occurredAt"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().Be(_now, "the TMS's clock is the test's");
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain("shipmentNumber", shipmentNumber);
    }

    [Fact]
    public async Task Record_WithNoEta_PublishesAShipmentCollectedWithNone()
    {
        var (_, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(Collected(reference, _now.AddMinutes(-1)), _cancellationToken, correlationId);

        var message = (await tms.PublishedAsync(correlationId, "ShipmentCollected", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        CanonicalModelSchemas.ValidateShipmentCollected(body).IsValid.Should().BeTrue();
        body["eta"].Should().BeNull();
    }

    [Fact]
    public async Task Record_TheSameEventIdAgain_IsARepeat_OnTheOriginalTrail_AndPublishesNothingNew()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        var trackingEvent = Collected(reference, _now.AddMinutes(-1), eventId: "evt-repeat");
        await tms.RecordedAsync(trackingEvent, _cancellationToken, correlationId);
        await tms.PublishedAsync(correlationId, "ShipmentCollected", _cancellationToken);

        var repeated = await tms.RecordedAsync(trackingEvent, _cancellationToken, TmsCalls.NewCorrelationId());

        // The original's trail, event ID and result come back, which shows the event was stored with both.
        repeated["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeated["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        repeated["eventId"]!.GetValue<string>().Should().Be("evt-repeat");
        repeated["movedShipment"]!.GetValue<bool>().Should().BeTrue();
        repeated["shipmentStatus"]!.GetValue<string>().Should().Be("Collected");
        (await tms.PublishedAsync(correlationId, "ShipmentCollected", _cancellationToken)).Should().ContainSingle();
        tms.Published.Count(m => m.Subject == "ShipmentCollected" && CarrierBookingCalls.NamesShipment(m, shipmentNumber)).Should().Be(1);
    }

    [Fact]
    public async Task Record_AnotherCollectedForACollectedShipment_IsStored_ButMovesAndPublishesNothing()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        await tms.RecordedAsync(Collected(reference, _now.AddMinutes(-2), eventId: "evt-a"), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var recorded = await tms.RecordedAsync(Collected(reference, _now.AddMinutes(-1), eventId: "evt-b"), _cancellationToken, correlationId);

        recorded["repeat"]!.GetValue<bool>().Should().BeFalse();
        recorded["movedShipment"]!.GetValue<bool>().Should().BeFalse();
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Collected");
        await Task.Delay(TimeSpan.FromSeconds(2), _cancellationToken); // Lets the outbox run, so a message that shouldn't be sent would show up.
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().BeEmpty("a change that wasn't made isn't published");
        tms.Published.Count(m => m.Subject == "ShipmentCollected" && CarrierBookingCalls.NamesShipment(m, shipmentNumber)).Should().Be(1);
    }

    [Fact]
    public async Task Record_AReferenceNoBookingOfTheCarrierHas_IsRefused_WithShipmentNotFound_AndPublishesNothing()
    {
        var correlationId = TmsCalls.NewCorrelationId();

        var problem = await tms.RecordRefusedAsync(Collected("NO-SUCH-REFERENCE", _now.AddMinutes(-1)), HttpStatusCode.NotFound, _cancellationToken, correlationId);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
        problem["correlationId"]!.GetValue<string>().Should().Be(correlationId);
    }

    [Fact]
    public async Task Record_ByAnotherCarrierWithTheReferenceOfAShipmentNotItsOwn_IsRefused_WithShipmentNotFound_AndChangesNothing()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);

        var problem = await tms.RecordRefusedAsync(
            Collected(reference, _now.AddMinutes(-1), source: CarrierSource(PlanningCalls.OtherCarrierId)), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked");
    }

    [Fact]
    public async Task Record_FromOwnFleet_IsRefused_WithShipmentNotFound_UntilP12FindsItsShipmentByTrip()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);

        var problem = await tms.RecordRefusedAsync(
            Collected(reference, _now.AddMinutes(-1), source: new JsonObject { ["type"] = "OwnFleet" }), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked");
    }

    [Fact]
    public async Task Record_AnEventTimeInTheFuture_IsRefused_WithEventTimeInTheFuture_AndChangesNothing()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);

        var problem = await tms.RecordRefusedAsync(
            Collected(reference, _now + ClockSkew + TimeSpan.FromSeconds(1)), HttpStatusCode.BadRequest, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("EventTimeInTheFuture");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked");
    }

    [Fact]
    public async Task Record_AnEventTimeWithinTheClockSkewAhead_IsAccepted()
    {
        var (_, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);

        var recorded = await tms.RecordedAsync(Collected(reference, _now + ClockSkew), _cancellationToken);

        recorded["movedShipment"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Record_ComparesTheEventTimeAsAnInstant_NotAsALocalClockTime()
    {
        var (_, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        // The local clock reads two hours ahead of the TMS's, but at +02:00 it is a moment ago.
        var eventTime = new DateTimeOffset(_now.UtcDateTime.AddMinutes(-1)).ToOffset(TimeSpan.FromHours(2));

        var recorded = await tms.RecordedAsync(Collected(reference, eventTime), _cancellationToken);

        recorded["movedShipment"]!.GetValue<bool>().Should().BeTrue();
    }

    public static TheoryData<string, Action<JsonObject>> NotTheCanonicalModel => new()
    {
        { "no location", body => body.Remove("location") },
        { "a field outside the Canonical Model", body => body["carrier"] = "CAR-TEST-0001" },
        { "a Carrier source with no Carrier ID", body => body["source"] = new JsonObject { ["type"] = "Carrier" } },
        { "an Own fleet source naming a Carrier", body => body["source"] = new JsonObject { ["type"] = "OwnFleet", ["carrierId"] = "CAR-TEST-0001" } },
        { "a type the TMS doesn't know", body => body["type"] = "Teleported" },
        { "a blank event ID", body => body["eventId"] = " " },
        { "a country that isn't alpha-2", body => body["location"]!["countryCode"] = "NLD" },
        { "no event time", body => body.Remove("eventTime") },
    };

    [Theory]
    [MemberData(nameof(NotTheCanonicalModel))]
#pragma warning disable xUnit1026 // The description names the case in the test's output.
    public async Task Record_AnEventThatIsNotTheCanonicalModel_IsABadRequest(string description, Action<JsonObject> spoil)
#pragma warning restore xUnit1026
    {
        var (_, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var trackingEvent = Collected(reference, _now.AddMinutes(-1));
        spoil(trackingEvent);

        using var response = await tms.RecordAsync(trackingEvent, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, description);
    }

    [Fact]
    public async Task Record_WithoutACorrelationId_IsABadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/tracking-events", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(Collected("VDT-1", _now.AddMinutes(-1))),
        };

        using var response = await tms.TrackingRecord.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void TheEventsTheTestsSend_AreTheCanonicalModelsTrackingEvent()
    {
        var results = CanonicalModelSchemas.ValidateTrackingEvent(Collected("VDT-1", _now.AddMinutes(-1), eta: _now.AddDays(1)));

        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        CanonicalModelSchemas.ValidateTrackingEvent(Collected("VDT-1", _now, source: new JsonObject { ["type"] = "OwnFleet" })).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheCanonicalModelsTrackingEvent_RefusesWhatTheApiRefuses()
    {
        var withoutLocation = Collected("VDT-1", _now);
        withoutLocation.Remove("location");

        CanonicalModelSchemas.ValidateTrackingEvent(withoutLocation).IsValid.Should().BeFalse();
        CanonicalModelSchemas.ValidateTrackingEvent(Collected("VDT-1", _now, source: new JsonObject { ["type"] = "Carrier" })).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TheCanonicalModel_HasTheFourShipmentEventsOfP5()
    {
        JsonObject Sample(string eventType, bool exception = false)
        {
            var sample = new JsonObject
            {
                ["eventId"] = Guid.NewGuid().ToString(),
                ["eventType"] = eventType,
                ["eventVersion"] = 1,
                ["occurredAt"] = JsonValue.Create(_now),
                ["correlationId"] = "corr",
                ["shipmentNumber"] = "SH-0000001",
                ["transportOrderNumber"] = "TO-0000001",
                ["carrierId"] = "CAR-0001",
                ["carrierBookingReference"] = "VDT-1",
                ["eventTime"] = JsonValue.Create(_now),
                ["location"] = new JsonObject { ["place"] = "Rotterdam", ["postcode"] = "3011 AA", ["countryCode"] = "NL" },
                ["eta"] = null,
            };
            if (exception)
            {
                sample["exceptionType"] = "Damaged";
                sample["remark"] = null;
            }

            return sample;
        }

        CanonicalModelSchemas.ValidateShipmentCollected(Sample("ShipmentCollected")).IsValid.Should().BeTrue();
        CanonicalModelSchemas.ValidateShipmentInTransit(Sample("ShipmentInTransit")).IsValid.Should().BeTrue();
        CanonicalModelSchemas.ValidateShipmentDelivered(Sample("ShipmentDelivered")).IsValid.Should().BeTrue();
        CanonicalModelSchemas.ValidateShipmentExceptionReported(Sample("ShipmentExceptionReported", exception: true)).IsValid.Should().BeTrue();
        CanonicalModelSchemas.ValidateShipmentDelivered(Sample("ShipmentCollected")).IsValid.Should().BeFalse("each schema is for its own event");
    }
}
