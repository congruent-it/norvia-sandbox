using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.EditShift;
using Norvia.Tms.Application.Fleet.Planning.ListOpenDays;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Api.Capacity;

public static class CapacityEndpoints
{
    public static IEndpointRouteBuilder MapCapacity(this IEndpointRouteBuilder app)
    {
        var capacity = app.NewVersionedApi("Capacity")
            .MapGroup("/v{version:apiVersion}/capacity")
            .HasApiVersion(1);

        // Like the other Planner lists, this answers any caller with a TMS token until it gets its own role.
        capacity.MapGet("/days", ListOpenDaysAsync)
            .RequireAuthorization()
            .WithName("ListOpenPlanningDays")
            .WithSummary("List the open days of the Planning horizon, one per day, earliest first.")
            .WithDescription(
                "The horizon job opens today and the next days at 00:00 Europe/Amsterdam (14 by default) and closes the days before today, which leave the list. "
                + "Each day shows how many Vehicles are available and which Drivers work, with their Shift start and end and whether the Planner edited it by hand.");

        // The Planner edits from the TMS UI. P10's actions have no correlation ID: they publish nothing and start no Message trail.
        capacity.MapPut("/days/{date}/shifts/{driverId}", EditShiftAsync)
            .WithName("EditShift")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Set one Driver's Shift on an open day.")
            .WithDescription(
                "start and end are required, and the Shift ends after it starts; otherwise the call is a 400. The Shift replaces the one from the Driver's Roster, "
                + "or adds one on a day off, and is marked as edited by hand. A change that breaks a rule is refused, changing nothing, with a problem whose code says which: "
                + "PlanningDayNotFound (404), DriverNotFound (404) or DayClosed (409): days before today are closed and read-only.");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<PlanningDayView>>> ListOpenDaysAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListOpenPlanningDaysQuery(), cancellationToken));

    private static async Task<Results<Ok<EditShiftResult>, ProblemHttpResult, ValidationProblem>> EditShiftAsync(
        DateOnly date,
        string driverId,
        EditShiftRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Start is null)
        {
            errors[nameof(EditShiftRequest.Start)] = ["A Shift needs its start."];
        }

        if (request.End is null)
        {
            errors[nameof(EditShiftRequest.End)] = ["A Shift needs its end."];
        }

        if (errors.Count > 0 || request is not { Start: { } start, End: { } end })
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (start >= end)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(EditShiftRequest.End)] = ["A Shift must end after it starts."] });
        }

        var result = await mediator.Send(new EditShiftCommand(date, driverId, start, end), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodeOf(Enum.Parse<PlanningRefusalCode>(refusal.ErrorCode)),
            title: "The Shift can't be changed.",
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }

    private static int StatusCodeOf(PlanningRefusalCode code) => code switch
    {
        PlanningRefusalCode.PlanningDayNotFound or PlanningRefusalCode.DriverNotFound => StatusCodes.Status404NotFound,
        PlanningRefusalCode.DayClosed => StatusCodes.Status409Conflict,
        PlanningRefusalCode.ShiftInvalid => StatusCodes.Status400BadRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The API doesn't know this refusal's status."),
    };
}
