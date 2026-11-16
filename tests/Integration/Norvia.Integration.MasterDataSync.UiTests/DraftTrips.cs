using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.UiTests;

/// <summary>Plans a Draft Trip on the Own fleet's Vehicle and Driver, as the Planner would, so a journey can show what Finance making one inactive does to it.</summary>
public static class DraftTrips
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>
    /// Makes a Draft Trip of a Released Shipment on a working day of the Planning horizon, with the Vehicle and the Driver, and returns the day and the Trip's number. A day whose Vehicle or Driver
    /// another Trip already uses is left for the next one.
    /// </summary>
    public static async Task<(DateOnly Day, string Number)> PlanAsync(this MasterDataSyncUiFixture tms, string vehicleId, string driverId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam).DateTime);
        for (var ahead = 3; ahead <= 13; ahead++)
        {
            var day = today.AddDays(ahead);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var shipment = await ReleasedAsync(tms, day, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
            {
                Content = JsonContent.Create(new
                {
                    day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    vehicleId,
                    driverId,
                    plannedStart = "09:00",
                    plannedDrivingDuration = "04:00:00",
                    shipmentNumbers = new[] { shipment },
                }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var response = await tms.TmsUi.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return (day, JsonNode.Parse(body)!["number"]!.GetValue<string>());
            }
        }

        throw new InvalidOperationException($"No working day of the Planning horizon takes a Trip of {vehicleId} and {driverId}.");
    }

    // A Released Own fleet Shipment that collects from 08:00 on the day, 2 pallets of 500 kg.
    private static async Task<string> ReleasedAsync(MasterDataSyncUiFixture tms, DateOnly day, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), Amsterdam.GetUtcOffset(day.ToDateTime(new TimeOnly(8, 0))));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(from);
        booking["goodsLines"] = new JsonArray { new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 500m, ["dimensionsCm"] = null } };
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", cancellationToken);
        var assigned = await PostAsync(tms, $"/v1/transport-orders/{number}/assign", JsonContent.Create(new { assignedTo = "OwnFleet" }), HttpStatusCode.Created, cancellationToken);
        var shipment = assigned["shipmentNumber"]!.GetValue<string>();
        await PostAsync(tms, $"/v1/shipments/{shipment}/release", content: null, HttpStatusCode.OK, cancellationToken);
        return shipment;
    }

    // As the Planner does through the TMS UI, under a key of its own.
    private static async Task<JsonNode> PostAsync(MasterDataSyncUiFixture tms, string path, HttpContent? content, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await tms.TmsUi.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(expected, body);
        return JsonNode.Parse(body)!;
    }
}
