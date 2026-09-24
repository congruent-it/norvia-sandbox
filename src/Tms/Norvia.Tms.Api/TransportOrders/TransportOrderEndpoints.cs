using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

public static class TransportOrderEndpoints
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    public static IEndpointRouteBuilder MapTransportOrders(this IEndpointRouteBuilder app)
    {
        var transportOrders = app.NewVersionedApi("TransportOrders")
            .MapGroup("/v{version:apiVersion}/transport-orders")
            .HasApiVersion(1)
            // Authorization for the intake Logic App (commands) and API Management (queries) arrives with #20.
            .AllowAnonymous();

        transportOrders.MapPost("/", SubmitAsync)
            .WithName("SubmitTransportOrder")
            .WithSummary("Submit a Transport Order (Canonical Model) on behalf of the Shipper it names.");

        transportOrders.MapGet("/{transportOrderNumber}", GetAsync)
            .WithName("GetTransportOrder");

        transportOrders.MapGet("/", ListAsync)
            .WithName("ListTransportOrders");

        return app;
    }

    private static async Task<Results<Created<SubmitTransportOrderResult>, ValidationProblem>> SubmitAsync(
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CanonicalTransportOrder transportOrder,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = CanonicalShape.Check(transportOrder, correlationId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new SubmitTransportOrderCommand(transportOrder, correlationId!), cancellationToken);
        return TypedResults.Created($"/v1/transport-orders/{result.Value.TransportOrderNumber}", result.Value);
    }

    private static async Task<Results<Ok<TransportOrderView>, ProblemHttpResult>> GetAsync(
        string transportOrderNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransportOrderQuery(transportOrderNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<TransportOrderSummary>>> ListAsync(
        [FromQuery] TransportOrderStatus status,
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListTransportOrdersQuery(status), cancellationToken));
}
