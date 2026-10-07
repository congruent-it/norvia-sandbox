using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;

namespace Norvia.Tms.Api.ShipperTracking;

public static class ShipperTrackingEndpoints
{
    public const string ShipperIdHeader = "X-Shipper-ID";

    /// <summary>
    /// The app role only API Management's managed identity holds. It is separate from the Planner's permissions, so a
    /// Shipper-scoped call can never reach the Planner's views.
    /// </summary>
    public const string ReadRole = "ShipperTracking.Read";

    public static IEndpointRouteBuilder MapShipperTracking(this IEndpointRouteBuilder app)
    {
        var shipperTracking = app.NewVersionedApi("ShipperTracking")
            .MapGroup("/v{version:apiVersion}/shipper-tracking")
            .HasApiVersion(1);

        // The Shipper ID is trusted only because this caller sends it: it comes from the validated token at API Management (ADR 0005).
        shipperTracking.MapGet("/transport-orders", ListTransportOrdersAsync)
            .WithName("ListShipperTransportOrders")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ShipperTracking))
            .WithSummary("List a Shipper's Transport Orders with their Tracking status (Canonical Model), newest collection first.")
            .WithDescription(
                $"Scoped to the Shipper in {ShipperIdHeader}, which is required, as is {TrackingScopeMiddleware.CorrelationIdHeader}; otherwise the call is a 400. "
                + "Each item has the Transport Order number, the Shipper reference, the collection and delivery places, the Tracking status "
                + "(Received, Scheduled, Collected, InTransit or Delivered), the ETA and the last update. The Tracking status is worked out when the call is made. "
                + "Rejected bookings are not returned. Nothing in the answer names a Carrier, a Shipment number, an Exception or Stale.");

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<ShipperTransportOrderListItem>>, ValidationProblem>> ListTransportOrdersAsync(
        [FromHeader(Name = ShipperIdHeader)] string? shipperId,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(shipperId))
        {
            errors[ShipperIdHeader] = [$"A Shipper's query needs the {ShipperIdHeader} API Management took from the token."];
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TrackingScopeMiddleware.CorrelationIdHeader] = [$"A Shipper's query needs a {TrackingScopeMiddleware.CorrelationIdHeader}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await mediator.Send(new ListShipperTransportOrdersQuery(shipperId!), cancellationToken));
    }
}
