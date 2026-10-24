using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.Capacity;
using Norvia.Tms.TestSupport.Planning;

namespace Norvia.Tms.TestSupport.Trips;

/// <summary>A Trip the Planner dispatched for today, with the one Shipment on it.</summary>
public sealed record DispatchedTrip(string TripNumber, string ShipmentNumber, string DriverId);

/// <summary>What the Driver journeys do at the TMS's boundary to give a Driver a Trip: plan an Own fleet Shipment, put it on a Trip for today and dispatch it.</summary>
public static class TripCalls
{
    private static readonly TimeSpan TooCloseToMidnight = new(23, 30, 0);
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Whether today ends too soon to plan a Trip for it: a Trip is planned for today, and its Shipment collects a few minutes from now.</summary>
    public static bool TodayIsAboutToEnd() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam).TimeOfDay > TooCloseToMidnight;

    /// <summary>
    /// A Released Own fleet Shipment and a Trip for today with the crew, dispatched. The Trip dispatch worker sends it to the Driver app. A crew is one Driver and one Vehicle, so two tests
    /// that run on the same day don't plan the same crew twice.
    /// </summary>
    public static async Task<DispatchedTrip> DispatchTripForTodayAsync(this TmsFixture tms, string driverId, string vehicleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tms);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam).DateTime);
        await tms.HorizonStartingAtAsync(today, cancellationToken);
        var shipment = await ReleasedOwnFleetShipmentAsync(tms, cancellationToken);

        using var created = await PostAsync(
            tms, "/v1/trips",
            JsonContent.Create(new { day = $"{today:yyyy-MM-dd}", vehicleId, driverId, plannedStart = "08:30", plannedDrivingDuration = "02:00:00", shipmentNumbers = new[] { shipment } }),
            cancellationToken, idempotencyKey: Guid.NewGuid().ToString());
        var trip = (await ReadAsync(created, HttpStatusCode.Created, cancellationToken))["number"]!.GetValue<string>();
        using var dispatched = await PostAsync(tms, $"/v1/trips/{trip}/dispatch", content: null, cancellationToken);
        await ReadAsync(dispatched, HttpStatusCode.OK, cancellationToken);
        return new DispatchedTrip(trip, shipment, driverId);
    }

    // A Released Own fleet Shipment that collects from a few minutes from now, 2 pallets of 500 kg: the TMS rejects a collection window that starts in the past, and the Driver app lets a Driver
    // start a Trip on its day only, so the Trip is for today and its Shipment collects later today.
    private static async Task<string> ReleasedOwnFleetShipmentAsync(TmsFixture tms, CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddMinutes(5);
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 500m, ["dimensionsCm"] = null } };
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", cancellationToken);
        var shipment = (await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), cancellationToken))["shipmentNumber"]!.GetValue<string>();
        await tms.ReleasedAsync(shipment, cancellationToken);
        return shipment;
    }

    private static async Task<HttpResponseMessage> PostAsync(TmsFixture tms, string path, HttpContent? content, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(expected, body);
        return JsonNode.Parse(body)!;
    }
}
