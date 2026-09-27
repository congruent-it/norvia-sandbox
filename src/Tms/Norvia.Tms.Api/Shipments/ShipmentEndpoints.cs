using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;

namespace Norvia.Tms.Api.Shipments;

public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipments(this IEndpointRouteBuilder app)
    {
        // Like the Transport Order reads, these answer any caller with a TMS token until they get their own role.
        var shipments = app.NewVersionedApi("Shipments")
            .MapGroup("/v{version:apiVersion}/shipments")
            .HasApiVersion(1)
            .RequireAuthorization();

        shipments.MapGet("/{shipmentNumber}", GetAsync)
            .WithName("GetShipment")
            .WithSummary("Read a Shipment: its status, its Transport Order and who carries it, Own fleet or a Carrier.");

        return app;
    }

    private static async Task<Results<Ok<ShipmentView>, ProblemHttpResult>> GetAsync(
        string shipmentNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetShipmentQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} not found.")
            : TypedResults.Ok(result.Value);
    }
}
