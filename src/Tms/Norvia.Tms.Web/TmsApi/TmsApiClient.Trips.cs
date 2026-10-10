using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.TmsApi;

// The Planner's edits of Draft Trips (P11). They publish nothing and start no Message trail, so an answer carries no
// correlation ID and the TMS UI only logs it, in a span of its own.
internal sealed partial class TmsApiClient
{
    public Task<TripAnswer> CreateDraftTripAsync(TripEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SendTripAsync("Create Draft Trip", HttpMethod.Post, "/v1/trips", tripNumber: null, new CreateTripBody(entry.Day, entry.VehicleId, entry.DriverId, entry.PlannedStart, entry.PlannedDrivingDuration), cancellationToken);
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

    public Task<TripAnswer> DiscardDraftTripAsync(string number, CancellationToken cancellationToken) =>
        SendTripAsync("Discard Draft Trip", HttpMethod.Delete, $"/v1/trips/{Uri.EscapeDataString(number)}", number, null, cancellationToken);

    private async Task<TripAnswer> SendTripAsync(string action, HttpMethod method, string path, string? tripNumber, object? body, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(action, ActivityKind.Internal);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        if (method == HttpMethod.Post)
        {
            // The standard resilience handler retries a call, so the same Draft is never created twice.
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

                return new TripAnswer.Created(created.Number);
            case HttpStatusCode.NoContent:
                using (Track(Flows.TripPlanning, TrackingKeys.Succeeded, new() { TripNumber = tripNumber }))
                {
                    LogTripEdited(action);
                }

                return new TripAnswer.Done();
            // A refused action: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.BadRequest:
                var (problem, reason) = await ReadRefusalAsync(response, $"{action} was refused.", cancellationToken);
                using (Track(Flows.TripPlanning, TrackingKeys.Refused, new() { TripNumber = tripNumber, FailureCode = problem?.Code }))
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
    private sealed record CreateTripBody(DateOnly Day, string? VehicleId, string? DriverId, TimeOnly? PlannedStart, TimeSpan? PlannedDrivingDuration);

    private sealed record ChangeTripBody(string? VehicleId, string? DriverId, TimeOnly? PlannedStart, TimeSpan? PlannedDrivingDuration);

    private sealed record CreatedTrip(string Number);
}
