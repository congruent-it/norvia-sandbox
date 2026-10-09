using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Web.Capacity;

namespace Norvia.Tms.Web.TmsApi;

// The Planner's edits of the Own fleet's availability (P10). They publish nothing and start no Message trail, so an answer
// carries no correlation ID and the TMS UI only logs it, in a span of its own.
internal sealed partial class TmsApiClient
{
    public Task<CapacityAnswer> RecordAbsenceAsync(PeriodEntry entry, CancellationToken cancellationToken) =>
        EditCapacityAsync("Record Absence", HttpMethod.Post, "/v1/capacity/absences", ToBody(entry), cancellationToken);

    public Task<CapacityAnswer> RemoveAbsenceAsync(Guid id, CancellationToken cancellationToken) =>
        EditCapacityAsync("Remove Absence", HttpMethod.Delete, $"/v1/capacity/absences/{id}", null, cancellationToken);

    public Task<CapacityAnswer> RecordOffTheRoadAsync(PeriodEntry entry, CancellationToken cancellationToken) =>
        EditCapacityAsync("Record Off the road days", HttpMethod.Post, "/v1/capacity/off-the-road-days", ToBody(entry), cancellationToken);

    public Task<CapacityAnswer> RemoveOffTheRoadAsync(Guid id, CancellationToken cancellationToken) =>
        EditCapacityAsync("Remove Off the road day", HttpMethod.Delete, $"/v1/capacity/off-the-road-days/{id}", null, cancellationToken);

    public Task<CapacityAnswer> ChangeRosterAsync(string driverId, IReadOnlyList<RosterWeekdayView> weekdays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        var body = new RosterBody(weekdays.ToDictionary(weekday => weekday.Weekday.ToString(), weekday => new ShiftBody(weekday.Start, weekday.End)));
        return EditCapacityAsync("Change Roster", HttpMethod.Put, $"/v1/capacity/rosters/{Uri.EscapeDataString(driverId)}", body, cancellationToken);
    }

    public Task<CapacityAnswer> EditShiftAsync(DateOnly day, string driverId, TimeOnly startsAt, TimeOnly endsAt, CancellationToken cancellationToken) =>
        EditCapacityAsync(
            "Edit Shift",
            HttpMethod.Put,
            $"/v1/capacity/days/{day:yyyy-MM-dd}/shifts/{Uri.EscapeDataString(driverId)}",
            new ShiftBody(startsAt, endsAt),
            cancellationToken);

    private async Task<CapacityAnswer> EditCapacityAsync(string action, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(action, ActivityKind.Internal);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.NoContent:
                LogCapacityEdited(action);
                return new CapacityAnswer.Done();
            // A refused edit: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.BadRequest:
                var problem = await response.Content.ReadFromJsonAsync<ExceptionRefusal>(Json, cancellationToken);
                var reason = problem?.Detail
                    ?? (problem?.Errors is { Count: > 0 } errors ? string.Join(' ', errors.Values.SelectMany(messages => messages)) : null)
                    ?? $"{action} was refused.";
                LogCapacityRefused(action, problem?.Code ?? "unknown", reason);
                return new CapacityAnswer.Refused(reason);
            default:
                throw Unexpected(response, action);
        }
    }

    private static PeriodBody ToBody(PeriodEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new PeriodBody(entry.SubjectId, entry.From, entry.To, entry.Reason, entry.Remark);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} recorded")]
    private partial void LogCapacityEdited(string action);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} refused with {Code}: {Reason}")]
    private partial void LogCapacityRefused(string action, string code, string reason);

    // What the TMS UI asks the TMS API to record or change.
    private sealed record PeriodBody(string Id, DateOnly From, DateOnly To, string Reason, string Remark);

    private sealed record ShiftBody(TimeOnly Start, TimeOnly End);

    private sealed record RosterBody(Dictionary<string, ShiftBody> Weekdays);
}
