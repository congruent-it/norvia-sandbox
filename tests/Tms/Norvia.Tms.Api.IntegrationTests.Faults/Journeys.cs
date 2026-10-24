using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Norvia.Tms.TestSupport.Trips;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>What the fault tests set up and do: a Carrier's tracking message on its queue, and a Driver at work on a Trip dispatched for today.</summary>
internal sealed class Journeys(FaultsFixture fixture)
{
    // A valid 1x1 PNG.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Puts a Carrier's Collected message on the tracking queue as API Management does: the Carrier from the token as <c>partyId</c>, and a correlation ID.</summary>
    public async Task SendCollectedAsync(string carrierBookingReference, string correlationId, CancellationToken cancellationToken)
    {
        var collected = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddMinutes(-5), Amsterdam);
        var body = new JsonObject
        {
            ["bookingRef"] = carrierBookingReference,
            ["eventId"] = Guid.NewGuid().ToString(),
            ["statusCode"] = "PU",
            ["eventTime"] = collected.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["place"] = "Rotterdam",
            ["postcode"] = "3011 AA",
            ["country"] = "NLD",
        };
        await using var sender = fixture.EdgeBus.CreateSender(FaultsFixture.TrackingQueue);
        var message = new ServiceBusMessage(body.ToJsonString()) { MessageId = Guid.NewGuid().ToString(), CorrelationId = correlationId, ContentType = "application/json" };
        message.ApplicationProperties["partyId"] = PlanningCalls.CarrierId;
        message.ApplicationProperties["correlationId"] = correlationId;
        await sender.SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// A Released Own fleet Shipment and a Trip for today with the crew, dispatched, which the Trip dispatch worker sends to the Driver app. Returns once the Driver has it.
    /// A crew is one Driver and one Vehicle, so two tests that run on the same day don't plan the same crew twice.
    /// </summary>
    public async Task<DispatchedTrip> DispatchedTripAsync(string driverId, string vehicleId, CancellationToken cancellationToken)
    {
        Assert.SkipWhen(TripCalls.TodayIsAboutToEnd(), "A Trip is planned for today, and today is about to end.");
        var dispatched = await fixture.DispatchTripForTodayAsync(driverId, vehicleId, cancellationToken);

        using var driver = fixture.Driver(driverId);
        await Eventually.Async(async () => (await TripsOfAsync(driver, cancellationToken)).FirstOrDefault(item => item!["tripNumber"]!.GetValue<string>() == dispatched.TripNumber), cancellationToken);
        return dispatched;
    }

    /// <summary>The Driver presses Start Trip, and waits for the TMS to record it. Returns once the Driver app says so.</summary>
    public async Task<string> StartedAsync(DispatchedTrip trip, CancellationToken cancellationToken)
    {
        var correlationId = await ActAsync(trip, "start", content: null, cancellationToken);
        await ReportStateAsync(trip, "StartTrip", "Recorded", cancellationToken);
        return correlationId;
    }

    /// <summary>The Driver reports the collection of the Shipment. Returns the correlation ID of the action.</summary>
    public Task<string> CollectedAsync(DispatchedTrip trip, CancellationToken cancellationToken) =>
        ActAsync(trip, $"stops/{trip.ShipmentNumber}/collection/collected", JsonContent.Create(new { }), cancellationToken);

    /// <summary>The Driver delivers the Shipment with a Proof of delivery: a signature and the given number of photos. Returns the correlation ID of the action.</summary>
    public Task<string> DeliveredAsync(DispatchedTrip trip, int photos, CancellationToken cancellationToken) =>
        ActAsync(
            trip, $"stops/{trip.ShipmentNumber}/delivery/deliver",
            JsonContent.Create(new
            {
                receivedBy = "Consignee",
                condition = "Clean",
                signature = new { contentType = "image/png", content = Png },
                photos = Enumerable.Range(0, photos).Select(_ => new { contentType = "image/png", content = Png }).ToArray(),
            }),
            cancellationToken);

    /// <summary>Waits for the Driver app to show the Driver's report of the kind in the state, and returns it with its failure code and message.</summary>
    public async Task<JsonObject> ReportStateAsync(DispatchedTrip trip, string kind, string state, CancellationToken cancellationToken)
    {
        using var driver = fixture.Driver(trip.DriverId);
        return await Eventually.Async(
            async () => (await TripsOfAsync(driver, cancellationToken)).Single(item => item!["tripNumber"]!.GetValue<string>() == trip.TripNumber)!["reports"]!.AsArray().OfType<JsonObject>()
                .FirstOrDefault(report => report["kind"]!.GetValue<string>() == kind && report["state"]!.GetValue<string>() == state),
            cancellationToken);
    }

    /// <summary>The Driver's report of the kind as the Driver app shows it now, without waiting for it to change.</summary>
    public async Task<JsonObject> ReportAsync(DispatchedTrip trip, string kind, CancellationToken cancellationToken)
    {
        using var driver = fixture.Driver(trip.DriverId);
        return (await TripsOfAsync(driver, cancellationToken)).Single(item => item!["tripNumber"]!.GetValue<string>() == trip.TripNumber)!["reports"]!.AsArray().OfType<JsonObject>()
            .Single(report => report["kind"]!.GetValue<string>() == kind);
    }

    public async Task<string> ShipmentStatusAsync(string shipmentNumber, CancellationToken cancellationToken) =>
        (await fixture.GetShipmentAsync(shipmentNumber, cancellationToken))["status"]!.GetValue<string>();

    private async Task<string> ActAsync(DispatchedTrip trip, string action, HttpContent? content, CancellationToken cancellationToken)
    {
        using var driver = fixture.Driver(trip.DriverId);
        using var response = await driver.PostAsync($"/v1/driver/trips/{trip.TripNumber}/{action}", content, cancellationToken);
        return (await ReadAsync(response, HttpStatusCode.OK, cancellationToken))["correlationId"]!.GetValue<string>();
    }

    private static async Task<JsonArray> TripsOfAsync(HttpClient driver, CancellationToken cancellationToken) =>
        (await driver.GetFromJsonAsync<JsonArray>("/v1/driver/trips", cancellationToken))!;

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(expected, body);
        return JsonNode.Parse(body)!;
    }
}
