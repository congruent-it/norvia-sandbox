using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Application.ConsigneeTracking;
using Norvia.Tms.Application.ConsigneeTracking.GetCode;
using Norvia.Tms.Application.ConsigneeTracking.ReplaceCode;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>The Planner's two operations on a Transport Order's tracking code (ADR 0052), called from the TMS UI with the manage role.</summary>
internal static class TrackingCodeEndpoints
{
    public static RouteGroupBuilder MapTrackingCodes(this RouteGroupBuilder transportOrders)
    {
        transportOrders.MapGet("/{transportOrderNumber}/tracking-code", GetAsync)
            .WithName("GetTransportOrderTrackingCode")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Read a Transport Order's tracking code, which the Consignee's link carries.")
            .WithDescription(
                "The code is a credential, so it is in no other view of the Transport Order. It is null for an Accepted Transport Order from before codes existed, "
                + "until the Planner replaces it. A Transport Order that is unknown, or a Rejected booking, is a 404.");

        transportOrders.MapPost("/{transportOrderNumber}/tracking-code/replace", ReplaceAsync)
            .WithName("ReplaceTransportOrderTrackingCode")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Replace a Transport Order's tracking code; the old code stops working at once.")
            .WithDescription(
                "For a link that went to the wrong person. There is no revocation without a replacement. Returns the new code. Only the TMS UI calls it, and it is deliberately not idempotent: every call issues a new code and the previous one stops working, so a retry rotates the code again. "
                + "A Transport Order that is unknown, or a Rejected booking, is a 404.");
        return transportOrders;
    }

    private static async Task<Results<Ok<TransportOrderTrackingCodeView>, NotFound>> GetAsync(
        string transportOrderNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTrackingCodeQuery(transportOrderNumber), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<TransportOrderTrackingCodeView>, NotFound>> ReplaceAsync(
        string transportOrderNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ReplaceTrackingCodeCommand(transportOrderNumber), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound();
    }
}
