using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.ConsigneeNotifications;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Api.ConsigneeNotifications;

public static partial class ConsigneeNotificationEndpoints
{
    /// <summary>The app role only the Consignee notification Logic App's managed identity holds, to read where to send a Consignee's email.</summary>
    public const string ReadRole = "ConsigneeNotifications.Read";

    public static IEndpointRouteBuilder MapConsigneeNotifications(this IEndpointRouteBuilder app)
    {
        app.NewVersionedApi("ConsigneeNotifications")
            .MapGroup("/v{version:apiVersion}/consignee-notifications")
            .HasApiVersion(1)
            .MapGet("/{transportOrderNumber}", GetAsync)
            .WithName("GetConsigneeNotification")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ConsigneeNotification))
            .WithSummary("Read the Consignee's email address and the tracking code for a Transport Order, for the Consignee notification Logic App.")
            .WithDescription(
                $"A {TrackingScopeMiddleware.CorrelationIdHeader} is required; otherwise the call is a 400. The ConsigneeNotificationRequested event names only the Transport Order, "
                + "because the address is personal data and the tracking code is a credential (ADR 0052), so the Logic App asks for both here when it sends the email. "
                + "A Transport Order with no usable email or no tracking code, or an unknown one, is a 404: there is nothing to send, and that is not an error. "
                + "Neither the address nor the code is ever logged.");
        return app;
    }

    private static async Task<Results<Ok<ConsigneeNotificationContent>, ValidationProblem, NotFound>> GetAsync(
        string transportOrderNumber,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        ILogger<TrackingScopeMiddleware> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [TrackingScopeMiddleware.CorrelationIdHeader] = [$"A Consignee notification needs a {TrackingScopeMiddleware.CorrelationIdHeader}."],
            });
        }

        var result = await mediator.Send(new GetConsigneeNotificationQuery(transportOrderNumber), cancellationToken);
        if (result.IsSuccess)
        {
            LogRead(logger, result.Value.TransportOrderNumber);
            return TypedResults.Ok(result.Value);
        }

        LogNothingToSend(logger);
        return TypedResults.NotFound();
    }

    // The Transport Order number only: the address and the tracking code are never logged.
    [LoggerMessage(Level = LogLevel.Information, Message = "The Consignee notification for Transport Order {TransportOrderNumber} was read")]
    private static partial void LogRead(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "A Consignee notification has nothing to send: the Transport Order has no usable email or tracking code, or isn't known")]
    private static partial void LogNothingToSend(ILogger logger);
}
