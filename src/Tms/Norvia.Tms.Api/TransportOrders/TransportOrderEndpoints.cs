using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetById;
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
            .WithSummary("Submit a Transport Order (Canonical Model) on behalf of the Shipper it names.")
            .WithDescription(
                "A booking that breaks Transport Order rules gets a 422 listing every reason. It is kept as Rejected, readable at the problem's instance. "
                + "Submitting the same booking again (same Shipper, Shipper reference and content) gets a 200 with the original Transport Order, "
                + "flagged as a repeat; reusing a Shipper reference for different content is rejected with ShipperReferenceReused.");

        transportOrders.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetTransportOrderById")
            .WithSummary("Read a Transport Order or a Rejected booking by its ID.");

        transportOrders.MapGet("/{transportOrderNumber}", GetAsync)
            .WithName("GetTransportOrder");

        transportOrders.MapGet("/", ListAsync)
            .WithName("ListTransportOrders");

        return app;
    }

    private static async Task<Results<Created<AcceptedTransportOrder>, Ok<AcceptedTransportOrder>, ProblemHttpResult, ValidationProblem>> SubmitAsync(
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

        var result = (await mediator.Send(new SubmitTransportOrderCommand(transportOrder, correlationId!), cancellationToken)).Value;
        if (result.Status == TransportOrderStatus.Rejected)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The Transport Order was rejected.",
                detail: "The booking breaks one or more Transport Order rules. Every reason is listed.",
                instance: $"/v1/transport-orders/{result.Id}",
                extensions: new Dictionary<string, object?> { ["reasons"] = result.RejectionReasons });
        }

        var accepted = new AcceptedTransportOrder(result.TransportOrderNumber!, result.Status, result.Repeat);
        return result.Repeat
            ? TypedResults.Ok(accepted)
            : TypedResults.Created($"/v1/transport-orders/{result.TransportOrderNumber}", accepted);
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

    private static async Task<Results<Ok<TransportOrderView>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransportOrderByIdQuery(id), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {id} not found.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<TransportOrderSummary>>> ListAsync(
        [FromQuery] TransportOrderStatus status,
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListTransportOrdersQuery(status), cancellationToken));
}
