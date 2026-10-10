using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.TmsApi;

// The Planner's edits of Draft Trips and the Shipments on them (P11). They publish nothing and start no Message trail, so an answer carries no
// correlation ID and the TMS UI only logs it, in a span of its own.
internal sealed partial class TmsApiClient
{
    public Task<TripAnswer> CreateDraftTripAsync(TripEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SendTripAsync("Create Draft Trip", HttpMethod.Post, "/v1/trips", tripNumber: null, new CreateTripBody(entry.Day, entry.VehicleId, entry.DriverId, entry.PlannedStart, entry.PlannedDrivingDuration, entry.ShipmentNumbers), cancellationToken);
    }

    public Task<TripAnswer> ChangeDraftTripAsync(string number, TripEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SendTripAsync(
            "Change Draft Trip",
            HttpMethod.Put,
            $"/v1/trips/{Uri.EscapeDataString(number)}",
            number,
            new ChangeTripBody(entry.VehicleId, entry.DriverId, entry.PlannedStart, entry.PlannedDrivingDuration),
            cancellationToken);
    }

    public Task<TripAnswer> AddShipmentsToTripAsync(string number, IReadOnlyList<string> shipmentNumbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumbers);
        return SendTripAsync("Add Shipments to Trip", HttpMethod.Post, $"/v1/trips/{Uri.EscapeDataString(number)}/shipments", number, new AddShipmentsBody(shipmentNumbers), cancellationToken);
    }

    public Task<TripAnswer> ReorderTripStopsAsync(string number, IReadOnlyList<StopReference> stops, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stops);
        return SendTripAsync("Reorder Trip Stops", HttpMethod.Put, $"/v1/trips/{Uri.EscapeDataString(number)}/stops", number, new ReorderStopsBody(stops), cancellationToken);
    }

    public Task<TripAnswer> DispatchTripAsync(string number, CancellationToken cancellationToken) =>
        SendTripAsync("Dispatch Trip", HttpMethod.Post, $"/v1/trips/{Uri.EscapeDataString(number)}/dispatch", number, null, cancellationToken);

    public Task<TripAnswer> DiscardDraftTripAsync(string number, CancellationToken cancellationToken) =>
        SendTripAsync("Discard Draft Trip", HttpMethod.Delete, $"/v1/trips/{Uri.EscapeDataString(number)}", number, null, cancellationToken);

    private async Task<TripAnswer> SendTripAsync(string action, HttpMethod method, string path, string? tripNumber, object? body, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(action, ActivityKind.Internal);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        if (method == HttpMethod.Post && tripNumber is null)
        {
            // The standard resilience handler retries a call, so the same Draft is never created twice. Adding Shipments needs none: adding one already on the Trip changes nothing.
            request.Headers.Add(IdempotencyKeyHeader, Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Created:
                var created = await response.Content.ReadFromJsonAsync<CreatedTrip>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a created Trip without a body.");
                using (Track(Flows.TripPlanning, TrackingKeys.Succeeded, new() { TripNumber = created.Number }))
                {
                    LogTripEdited(action);
                }

                return new TripAnswer.Created(created.Number, created.Warnings is { Count: > 0 } warnings ? warnings : null);
            case HttpStatusCode.OK when method == HttpMethod.Post && path.EndsWith("/dispatch", StringComparison.Ordinal):
                var dispatched = await response.Content.ReadFromJsonAsync<DispatchedTrip>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a dispatched Trip without a body.");
                using (Track(Flows.TripDispatch, TrackingKeys.Succeeded, new() { TripNumber = tripNumber, CorrelationId = dispatched.CorrelationId }))
                {
                    LogTripEdited(action);
                }

                return new TripAnswer.Dispatched(dispatched.Version, dispatched.CorrelationId);
            case HttpStatusCode.OK:
                var added = await response.Content.ReadFromJsonAsync<AddedShipments>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered added Shipments without a body.");
                using (Track(Flows.TripPlanning, TrackingKeys.Succeeded, new() { TripNumber = tripNumber }))
                {
                    LogTripEdited(action);
                }

                return new TripAnswer.Added(added.Warnings ?? []);
            case HttpStatusCode.NoContent:
                using (Track(Flows.TripPlanning, TrackingKeys.Succeeded, new() { TripNumber = tripNumber }))
                {
                    LogTripEdited(action);
                }

                return new TripAnswer.Done();
            // A refused action: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.BadRequest:
                var (problem, reason) = await ReadRefusalAsync(response, $"{action} was refused.", cancellationToken);
                using (Track(path.EndsWith("/dispatch", StringComparison.Ordinal) ? Flows.TripDispatch : Flows.TripPlanning, TrackingKeys.Refused, new() { TripNumber = tripNumber, FailureCode = problem?.Code }))
                {
                    LogTripRefused(action, problem?.Code ?? "unknown", reason);
                }

                return new TripAnswer.Refused(reason);
            default:
                throw Unexpected(response, action);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} done")]
    private partial void LogTripEdited(string action);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} refused with {Code}: {Reason}")]
    private partial void LogTripRefused(string action, string code, string reason);

    // What the TMS UI asks the TMS API to do, and what it answers.
    private sealed record CreateTripBody(DateOnly Day, string? VehicleId, string? DriverId, TimeOnly? PlannedStart, TimeSpan? PlannedDrivingDuration, IReadOnlyList<string>? ShipmentNumbers);

    private sealed record AddShipmentsBody(IReadOnlyList<string> ShipmentNumbers);

    private sealed record ReorderStopsBody(IReadOnlyList<StopReference> Stops);

    private sealed record DispatchedTrip(string Number, int Version, string CorrelationId);

    private sealed record AddedShipments(string Number, List<TripWarning>? Warnings);

    private sealed record ChangeTripBody(string? VehicleId, string? DriverId, TimeOnly? PlannedStart, TimeSpan? PlannedDrivingDuration);

    private sealed record CreatedTrip(string Number, List<TripWarning>? Warnings = null);
}
