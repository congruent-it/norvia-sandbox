using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ShipperNotifications.Report;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Api.ShipperNotifications;

public static class ShipperNotificationEndpoints
{
    /// <summary>The app role only the P7 Shipper notification Logic App's managed identity holds, for its sent and not-delivered reports.</summary>
    public const string ReportRole = "ShipperNotifications.Report";

    public static IEndpointRouteBuilder MapShipperNotifications(this IEndpointRouteBuilder app)
    {
        var notifications = app.NewVersionedApi("ShipperNotifications")
            .MapGroup("/v{version:apiVersion}/shipper-notifications")
            .HasApiVersion(1);

        notifications.MapPost("/{notificationId}/report-sent", ReportSentAsync)
            .WithName("ReportShipperNotificationSent")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.ExceptionHandling))
            .WithSummary("Report that a Requested Shipper notification was acknowledged by the Shipper's endpoint.")
            .WithDescription(
                "Moves the notification to Sent, records the time and publishes nothing. A report for a notification that is no longer Requested "
                + "(a repeat, or one that arrives after the other report) is ignored and logged: it changes and publishes nothing. "
                + "The X-Correlation-ID is the correlation ID of the Planner's action, which the ShipperNotificationRequested event carried. "
                + "ShipperNotificationNotFound (404, with that code) for an unknown notification ID.");

        notifications.MapPost("/{notificationId}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportShipperNotificationNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportRole))
            .WithMetadata(new TrackedFlow(Flows.ExceptionHandling))
            .WithSummary("Report that a Requested Shipper notification never reached the Shipper, with its technical cause.")
            .WithDescription(
                "Moves the notification to Not delivered with the technical cause and publishes ShipperNotificationNotDelivered, in one transaction. "
                + "A report for a notification that is no longer Requested is ignored and logged: it changes and publishes nothing. "
                + "ShipperNotificationNotFound (404, with that code) for an unknown notification ID.");

        return app;
    }

    private static async Task<Results<Ok<ShipperNotificationReportResult>, ProblemHttpResult, ValidationProblem>> ReportSentAsync(
        string notificationId,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(CorrelationIdMissing());
        }

        var result = await mediator.Send(new ReportShipperNotificationSentCommand(notificationId, correlationId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, correlationId);
    }

    private static async Task<Results<Ok<ShipperNotificationReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string notificationId,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        ShipperNotificationNotDeliveredRequest report,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors = CorrelationIdMissing();
        }

        if (report.StatusCode is null)
        {
            errors[nameof(ShipperNotificationNotDeliveredRequest.StatusCode)] = ["A not-delivered report needs the technical cause's status code."];
        }

        if (string.IsNullOrWhiteSpace(report.Message))
        {
            errors[nameof(ShipperNotificationNotDeliveredRequest.Message)] = ["A not-delivered report needs the technical cause's message."];
        }

        if (errors.Count > 0 || correlationId is null || report is not { StatusCode: { } statusCode, Message: { } message })
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new ReportShipperNotificationNotDeliveredCommand(notificationId, correlationId, statusCode, message), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, correlationId);
    }

    private static Dictionary<string, string[]> CorrelationIdMissing() =>
        new() { [TransportOrderEndpoints.CorrelationIdHeader] = [$"Reporting a Shipper notification needs an {TransportOrderEndpoints.CorrelationIdHeader}."] };

    private static ProblemHttpResult Refused<T>(Ardalis.Result.Result<T> result, string correlationId) =>
        RefusalProblem.Of<ExceptionRefusalCode, T>(result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);
}
