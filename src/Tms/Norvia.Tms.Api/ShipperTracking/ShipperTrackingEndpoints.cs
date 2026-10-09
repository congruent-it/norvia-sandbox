using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Application.ShipperTracking.ReadProofOfDeliveryFile;
using Norvia.Tms.Domain.ShipperTracking;

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
                + "It can be narrowed with trackingStatus and a collectionFrom to collectionTo date range (both ends included, by the date the collection window opens), "
                + "and is paged with page (from 1) and pageSize (1 to 100, 25 by default), always newest collection first. "
                + "Rejected bookings are not returned. Nothing in the answer names a Carrier, a Shipment number, an Exception or Stale.");

        shipperTracking.MapGet("/transport-orders/find", FindTransportOrderAsync)
            .WithName("FindShipperTransportOrder")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ShipperTracking))
            .WithSummary("Find one of a Shipper's Transport Orders by the Shipper's own reference, with its tracking (Canonical Model).")
            .WithDescription(
                $"As the get operation, which it answers the same as. Scoped to the Shipper in {ShipperIdHeader}; the reference is unique per Shipper. "
                + "A reference that is unknown or another Shipper's is a 404 with the code TransportOrderNotFound, the same for both.");

        shipperTracking.MapGet("/transport-orders/{transportOrderNumber}", GetTransportOrderAsync)
            .WithName("GetShipperTransportOrder")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ShipperTracking))
            .WithSummary("Get one of a Shipper's Transport Orders by Norvia's number, with its tracking (Canonical Model).")
            .WithDescription(
                $"Scoped to the Shipper in {ShipperIdHeader}, which is required, as is {TrackingScopeMiddleware.CorrelationIdHeader}; otherwise the call is a 400. "
                + "Returns the collection and delivery places, the collection window, the delivery window (the agreed one when there is one), the Tracking status, "
                + "the ETA, the last update and the progress: only progress Tracking events, in event-time order, without remarks. "
                + "A Transport Order that is unknown or another Shipper's is a 404 with the code TransportOrderNotFound, the same for both. "
                + "Nothing in the answer names a Carrier, a Shipment number, an Exception or Stale.");

        shipperTracking.MapGet("/transport-orders/{transportOrderNumber}/proof-of-delivery/files/{fileId:guid}", GetProofOfDeliveryFileAsync)
            .WithName("GetShipperProofOfDeliveryFile")
            .RequireAuthorization(policy => policy.RequireRole(ReadRole))
            .WithMetadata(new TrackedFlow(Flows.ShipperTracking))
            .WithSummary("Read one file of a Shipper's Transport Order's current Proof of delivery, streamed with the content type it was sent with.")
            .WithDescription(
                $"Scoped to the Shipper in {ShipperIdHeader}, which is required, as is {TrackingScopeMiddleware.CorrelationIdHeader}; otherwise the call is a 400. "
                + "A Transport Order that is unknown or another Shipper's is a 404 with the code TransportOrderNotFound. A file of a superseded Proof of delivery, "
                + "a Transport Order with no Proof of delivery or a file it doesn't hold is a 404 with the code ProofOfDeliveryNotFound. "
                + "P6's Planner operation to read a Proof of delivery file is unchanged.");

        return app;
    }

    private static async Task<Results<FileStreamHttpResult, ValidationProblem, ProblemHttpResult>> GetProofOfDeliveryFileAsync(
        string transportOrderNumber,
        Guid fileId,
        [FromHeader(Name = ShipperIdHeader)] string? shipperId,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(shipperId, correlationId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new GetShipperProofOfDeliveryFileQuery(shipperId!, transportOrderNumber, fileId), cancellationToken);
        return result.IsSuccess
            ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.Name)
            : RefusalProblem.Of<ShipperTrackingRefusalCode, ProofOfDeliveryFileContent>(
                result, "The Proof of delivery file can't be read.", correlationId!, _ => StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<ShipperTransportOrderDetail>, ValidationProblem, ProblemHttpResult>> GetTransportOrderAsync(
        string transportOrderNumber,
        [FromHeader(Name = ShipperIdHeader)] string? shipperId,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(shipperId, correlationId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new GetShipperTransportOrderQuery(shipperId!, transportOrderNumber), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, correlationId!);
    }

    private static async Task<Results<Ok<ShipperTransportOrderDetail>, ValidationProblem, ProblemHttpResult>> FindTransportOrderAsync(
        [FromQuery] string? shipperReference,
        [FromHeader(Name = ShipperIdHeader)] string? shipperId,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(shipperId, correlationId);
        if (string.IsNullOrWhiteSpace(shipperReference))
        {
            errors["shipperReference"] = ["Finding a Transport Order needs the Shipper's reference."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new FindShipperTransportOrderQuery(shipperId!, shipperReference!), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result, correlationId!);
    }

    private static ProblemHttpResult Refused(Ardalis.Result.Result<ShipperTransportOrderDetail> result, string correlationId) =>
        RefusalProblem.Of<ShipperTrackingRefusalCode, ShipperTransportOrderDetail>(
            result, "The Transport Order can't be tracked.", correlationId, _ => StatusCodes.Status404NotFound);

    private static Dictionary<string, string[]> Check(string? shipperId, string? correlationId)
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

        return errors;
    }

    private static async Task<Results<Ok<IReadOnlyList<ShipperTransportOrderListItem>>, ValidationProblem>> ListTransportOrdersAsync(
        [FromHeader(Name = ShipperIdHeader)] string? shipperId,
        [FromHeader(Name = TrackingScopeMiddleware.CorrelationIdHeader)] string? correlationId,
        IMediator mediator,
        CancellationToken cancellationToken,
        [FromQuery] TrackingStatus? trackingStatus = null,
        [FromQuery] DateOnly? collectionFrom = null,
        [FromQuery] DateOnly? collectionTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ListShipperTransportOrdersQuery.DefaultPageSize)
    {
        var errors = Check(shipperId, correlationId);
        if (page < 1)
        {
            errors["page"] = ["The page is counted from 1."];
        }

        if (pageSize is < 1 or > ListShipperTransportOrdersQuery.MaxPageSize)
        {
            errors["pageSize"] = [$"The page size is between 1 and {ListShipperTransportOrdersQuery.MaxPageSize}."];
        }

        if (collectionFrom > collectionTo)
        {
            errors["collectionTo"] = ["The collection date range ends before it starts."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await mediator.Send(
            new ListShipperTransportOrdersQuery(shipperId!, trackingStatus, collectionFrom, collectionTo, page, pageSize), cancellationToken));
    }
}
