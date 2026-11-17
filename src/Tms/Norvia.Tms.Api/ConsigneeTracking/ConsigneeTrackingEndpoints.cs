using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.ConsigneeTracking.GetView;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Api.ConsigneeTracking;

public static partial class ConsigneeTrackingEndpoints
{
    /// <summary>
    /// The tracking code travels in a header, not the path, so no URL log of the TMS or API Management holds it (ADR 0052).
    /// </summary>
    public const string TrackingCodeHeader = "X-Tracking-Code";

    /// <summary>The app role only API Management's managed identity holds. It opens one Transport Order by its code and nothing else.</summary>
    public const string ReadRole = "ConsigneeTracking.Read";

    public static IEndpointRouteBuilder MapConsigneeTracking(this IEndpointRouteBuilder app)
    {
        app.NewVersionedApi("ConsigneeTracking")
            .MapGroup("/v{version:apiVersion}/consignee-tracking")
            .HasApiVersion(1)
            .MapGet("/view", GetViewAsync)
            .WithName("GetConsigneeView")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ConsigneeTracking))
            .WithSummary("Get a Consignee's view of the one Transport Order a tracking code opens (Canonical Model).")
            .WithDescription(
                $"The tracking code is in {TrackingCodeHeader}, and a {TrackingScopeMiddleware.CorrelationIdHeader} is required; otherwise the call is a 400. "
                + "Returns the collection and delivery places, the delivery window (the agreed one when there is one), the Tracking status, the ETA, the last update, "
                + "the progress Tracking events in event-time order and the current Proof of delivery without its files. "
                + "Nothing in the answer names a Shipper, a Shipper reference, a Carrier, a Shipment number, an Exception, a remark, Stale or a price. "
                + "A code that is malformed, unknown, expired or replaced is the same 404 with the code TrackingCodeNotFound, in status, body and shape. "
                + "The code is never logged: the logs carry the Transport Order number it resolved to, or none.");
        return app;
    }

    private static async Task<Results<Ok<ConsigneeView>, ValidationProblem, ProblemHttpResult>> GetViewAsync(
        [FromHeader(Name = TrackingCodeHeader)] string? trackingCode,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        ILogger<TrackingScopeMiddleware> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(trackingCode))
        {
            errors[TrackingCodeHeader] = [$"A Consignee's query needs its {TrackingCodeHeader}."];
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TrackingScopeMiddleware.CorrelationIdHeader] = [$"A Consignee's query needs a {TrackingScopeMiddleware.CorrelationIdHeader}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new GetConsigneeViewQuery(trackingCode!), cancellationToken);
        if (result.IsSuccess)
        {
            LogResolved(logger, result.Value.TransportOrderNumber);
            return TypedResults.Ok(result.Value);
        }

        LogMissed(logger);
        return RefusalProblem.Of<ConsigneeTrackingRefusalCode, ConsigneeView>(
            result, "We can't find this shipment.", correlationId!, _ => StatusCodes.Status404NotFound);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A Consignee opened Transport Order {TransportOrderNumber}")]
    private static partial void LogResolved(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "A Consignee's tracking code matched no Transport Order: none")]
    private static partial void LogMissed(ILogger logger);
}
