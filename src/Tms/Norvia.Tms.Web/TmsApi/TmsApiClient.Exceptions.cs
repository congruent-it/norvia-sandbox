using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Web.Exceptions;

namespace Norvia.Tms.Web.TmsApi;

// The Planner's actions on an Exception (P7). Each starts in the TMS UI, so each answer names a Message trail of its own.
internal sealed partial class TmsApiClient
{
    public Task<ExceptionAnswer> NotifyShipperAsync(string exceptionNumber, string message, CancellationToken cancellationToken) =>
        ActAsync("Notify Shipper", exceptionNumber, "notify-shipper", new NotifyShipperBody(message, PlannerName.Demo), "Shipper notified", cancellationToken);

    public Task<ExceptionAnswer> CancelCarrierBookingAsync(string exceptionNumber, string reason, CancellationToken cancellationToken) =>
        ActAsync("Cancel Carrier booking", exceptionNumber, "cancel-carrier-booking", new CancelBody(reason), "Carrier booking cancelled", cancellationToken);

    public Task<ExceptionAnswer> AgreeDeliveryWindowAsync(string exceptionNumber, DateTimeOffset earliest, DateTimeOffset latest, CancellationToken cancellationToken) =>
        ActAsync("Agree delivery window", exceptionNumber, "delivery-window", new WindowBody(earliest, latest), "Delivery window agreed", cancellationToken);

    public Task<ExceptionAnswer> AddNoteAsync(string exceptionNumber, string text, CancellationToken cancellationToken) =>
        ActAsync("Add Exception note", exceptionNumber, "notes", new NoteBody(text, PlannerName.Demo), "Note added", cancellationToken);

    public Task<ExceptionAnswer> ResolveAsync(string exceptionNumber, ExceptionOutcome outcome, string remark, CancellationToken cancellationToken) =>
        ActAsync("Resolve Exception", exceptionNumber, "resolve", new ResolveBody(outcome, remark), "Exception resolved", cancellationToken);

    private async Task<ExceptionAnswer> ActAsync(
        string spanName, string exceptionNumber, string action, object body, string done, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/exceptions/{Uri.EscapeDataString(exceptionNumber)}/{action}", cancellationToken);
        request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var acted = await response.Content.ReadFromJsonAsync<ExceptionActed>(Json, cancellationToken)
                    ?? throw new HttpRequestException($"The TMS API answered '{done}' without a body.");
                using (Track(Flows.ExceptionHandling, TrackingKeys.Succeeded, new() { CorrelationId = acted.CorrelationId, ShipmentNumber = acted.ShipmentNumber }))
                {
                    LogExceptionActed(done, exceptionNumber);
                }

                return new ExceptionAnswer.Done();
            // A refused action: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.BadRequest:
                var problem = await response.Content.ReadFromJsonAsync<ExceptionRefusal>(Json, cancellationToken);
                var reason = problem?.Detail
                    ?? (problem?.Errors is { Count: > 0 } errors ? string.Join(' ', errors.Values.SelectMany(messages => messages)) : null)
                    ?? $"Exception {exceptionNumber} can't be changed that way.";
                using (Track(Flows.ExceptionHandling, TrackingKeys.Refused, new() { FailureCode = problem?.Code, CorrelationId = problem?.CorrelationId }))
                {
                    LogExceptionRefused(spanName, exceptionNumber, reason);
                }

                return new ExceptionAnswer.Refused(reason);
            default:
                throw Unexpected(response, spanName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} on Exception {ExceptionNumber}")]
    private partial void LogExceptionActed(string action, string exceptionNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} on Exception {ExceptionNumber} refused: {Reason}")]
    private partial void LogExceptionRefused(string action, string exceptionNumber, string reason);

    // What the TMS UI asks the TMS API to do on an Exception.
    private sealed record NotifyShipperBody(string Message, string Planner);

    private sealed record CancelBody(string Reason);

    private sealed record WindowBody(DateTimeOffset Earliest, DateTimeOffset Latest);

    private sealed record NoteBody(string Text, string Planner);

    private sealed record ResolveBody(ExceptionOutcome Outcome, string Remark);

    // What the TMS API answers any of them, as far as the TMS UI reads it.
    private sealed record ExceptionActed(string? ShipmentNumber, string? CorrelationId);

    // A refusal (a problem with a code and a detail) or an invalid request (a validation problem with its errors).
    private sealed record ExceptionRefusal(string? Detail, string? Code, string? CorrelationId, Dictionary<string, string[]>? Errors);
}
