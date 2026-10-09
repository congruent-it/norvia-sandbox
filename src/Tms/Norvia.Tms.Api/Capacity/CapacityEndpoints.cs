using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.ChangeRoster;
using Norvia.Tms.Application.Fleet.Planning.EditShift;
using Norvia.Tms.Application.Fleet.Planning.ListAbsences;
using Norvia.Tms.Application.Fleet.Planning.ListOffTheRoadDays;
using Norvia.Tms.Application.Fleet.Planning.ListOpenDays;
using Norvia.Tms.Application.Fleet.Planning.ListRosters;
using Norvia.Tms.Application.Fleet.Planning.RecordAbsence;
using Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;
using Norvia.Tms.Application.Fleet.Planning.RemoveAbsence;
using Norvia.Tms.Application.Fleet.Planning.RemoveOffTheRoad;
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
        capacity.MapPut("/days/{date}/shifts/{id}", EditShiftAsync)
            .WithName("EditShift")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Set one Driver's Shift on an open day.")
            .WithDescription(
                "start and end are required, otherwise the call is a 400. The Shift replaces the one from the Driver's Roster, "
                + "or adds one on a day off, and is marked as edited by hand. A change that breaks a rule is refused, changing nothing, with a problem whose code says which: "
                + "PlanningDayNotFound (404), DriverNotFound (404), DayClosed (409): days before today are closed and read-only, DriverAbsent (409): the Driver has an Absence that day, or ShiftInvalid (400): a Shift ends after it starts.");

        capacity.MapGet("/absences", ListAbsencesAsync)
            .RequireAuthorization()
            .WithName("ListAbsences")
            .WithSummary("List the Absences of today and later, earliest first.");

        capacity.MapGet("/off-the-road-days", ListOffTheRoadDaysAsync)
            .RequireAuthorization()
            .WithName("ListOffTheRoadDays")
            .WithSummary("List the Off the road days of today and later, earliest first.");

        capacity.MapGet("/rosters", ListRostersAsync)
            .RequireAuthorization()
            .WithName("ListRosters")
            .WithSummary("List each active Driver's Roster.");

        capacity.MapPut("/rosters/{id}", ChangeRosterAsync)
            .WithName("ChangeRoster")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Change a Driver's Roster.")
            .WithDescription(
                "weekdays maps a weekday name (Monday to Sunday) to its Shift start and end; a weekday left out is a day off. "
                + "The TMS rewrites every open day that isn't edited by hand and has no Absence; a day edited by hand keeps its Shift. "
                + "A change that breaks a rule is refused, changing nothing, with a problem whose code says which: DriverNotFound (404) or ShiftInvalid (400).");

        capacity.MapPost("/absences", RecordAbsenceAsync)
            .WithName("RecordAbsence")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Record a Driver's Absence for one day or several.")
            .WithDescription(
                "id (the Driver), from, to (inclusive) and reason (Leave, Sickness or Training) are required; remark is free text. Days beyond the Planning horizon are accepted "
                + "and take effect when the day opens. An Absence is accepted even when a Trip would use the Driver. Recording a day again changes its reason and remark. "
                + "Refused, changing nothing, with a problem whose code says which: DriverNotFound (404), DayClosed (409), PeriodInvalid (400) or RemarkTooLong (400).");

        capacity.MapDelete("/absences/{id:guid}", RemoveAbsenceAsync)
            .WithName("RemoveAbsence")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Remove an Absence: the Driver works the Shift from their Roster again.");

        capacity.MapPost("/off-the-road-days", RecordOffTheRoadAsync)
            .WithName("RecordOffTheRoad")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Take a Vehicle off the road for one day or several.")
            .WithDescription(
                "id (the Vehicle), from, to (inclusive) and reason (Maintenance or Repair) are required; remark is free text. Days beyond the Planning horizon are accepted "
                + "and take effect when the day opens. Accepted even when a Trip would use the Vehicle. "
                + "Refused, changing nothing, with a problem whose code says which: VehicleNotFound (404), DayClosed (409), PeriodInvalid (400) or RemarkTooLong (400).");

        capacity.MapDelete("/off-the-road-days/{id:guid}", RemoveOffTheRoadAsync)
            .WithName("RemoveOffTheRoad")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Remove an Off the road day: the Vehicle is available again.");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<PlanningDayView>>> ListOpenDaysAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListOpenPlanningDaysQuery(), cancellationToken));

    private static async Task<Results<Ok<EditShiftResult>, ProblemHttpResult, ValidationProblem>> EditShiftAsync(
        DateOnly date,
        string id,
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

        var result = await mediator.Send(new EditShiftCommand(date, new DriverId(id), start, end), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return Refused(result.ValidationErrors.First(), "The Shift can't be changed.");
    }

    private static async Task<Ok<IReadOnlyList<AbsenceView>>> ListAbsencesAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListAbsencesQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<OffTheRoadDayView>>> ListOffTheRoadDaysAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListOffTheRoadDaysQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<RosterView>>> ListRostersAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListRostersQuery(), cancellationToken));

    private static async Task<Results<Ok<ChangeRosterResult>, ProblemHttpResult, ValidationProblem>> ChangeRosterAsync(
        string id,
        ChangeRosterRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var weekdays = new Dictionary<DayOfWeek, ShiftTimes>();
        var errors = new Dictionary<string, string[]>();
        foreach (var (name, shift) in request.Weekdays ?? new Dictionary<string, RosterShiftRequest?>())
        {
            if (!Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var weekday) || !Enum.IsDefined(weekday))
            {
                errors[name] = ["A weekday is Monday, Tuesday, Wednesday, Thursday, Friday, Saturday or Sunday."];
            }
            else if (shift is not { Start: { } start, End: { } end })
            {
                errors[name] = ["A Shift needs its start and its end."];
            }
            else if (start >= end)
            {
                errors[name] = ["A Shift must end after it starts."];
            }
            else
            {
                weekdays[weekday] = new ShiftTimes(start, end);
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new ChangeRosterCommand(new DriverId(id), weekdays), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors.First(), "The Roster can't be changed.");
    }

    private static async Task<Results<Ok<RecordAbsenceResult>, ProblemHttpResult, ValidationProblem>> RecordAbsenceAsync(
        RecordPeriodRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = PeriodErrors(request, "A Driver is needed.");
        if (!Enum.TryParse<AbsenceReason>(request.Reason, ignoreCase: true, out var reason) || !Enum.IsDefined(reason))
        {
            errors[nameof(RecordPeriodRequest.Reason)] = ["A reason is Leave, Sickness or Training."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new RecordAbsenceCommand(new DriverId(request.Id!), request.From!.Value, request.To!.Value, reason, request.Remark), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors.First(), "The Absence can't be recorded.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAbsenceAsync(Guid id, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveAbsenceCommand(new AbsenceId(id)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors.First(), "The Absence can't be removed.");
    }

    private static async Task<Results<Ok<RecordOffTheRoadResult>, ProblemHttpResult, ValidationProblem>> RecordOffTheRoadAsync(
        RecordPeriodRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = PeriodErrors(request, "A Vehicle is needed.");
        if (!Enum.TryParse<OffTheRoadReason>(request.Reason, ignoreCase: true, out var reason) || !Enum.IsDefined(reason))
        {
            errors[nameof(RecordPeriodRequest.Reason)] = ["A reason is Maintenance or Repair."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new RecordOffTheRoadCommand(new VehicleId(request.Id!), request.From!.Value, request.To!.Value, reason, request.Remark), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors.First(), "The Off the road day can't be recorded.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveOffTheRoadAsync(Guid id, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveOffTheRoadCommand(new OffTheRoadDayId(id)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors.First(), "The Off the road day can't be removed.");
    }

    private static Dictionary<string, string[]> PeriodErrors(RecordPeriodRequest request, string missingId)
    {
        Dictionary<string, string[]> errors = [];
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            errors[nameof(RecordPeriodRequest.Id)] = [missingId];
        }

        if (request.From is null)
        {
            errors[nameof(RecordPeriodRequest.From)] = ["A period needs its first day."];
        }

        if (request.To is null)
        {
            errors[nameof(RecordPeriodRequest.To)] = ["A period needs its last day."];
        }

        return errors;
    }

    private static ProblemHttpResult Refused(Ardalis.Result.ValidationError refusal, string title) =>
        TypedResults.Problem(
            statusCode: StatusCodeOf(Enum.Parse<PlanningRefusalCode>(refusal.ErrorCode)),
            title: title,
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });

    private static int StatusCodeOf(PlanningRefusalCode code) => code switch
    {
        PlanningRefusalCode.PlanningDayNotFound or PlanningRefusalCode.DriverNotFound or PlanningRefusalCode.VehicleNotFound
            or PlanningRefusalCode.AbsenceNotFound or PlanningRefusalCode.OffTheRoadDayNotFound => StatusCodes.Status404NotFound,
        PlanningRefusalCode.DayClosed or PlanningRefusalCode.DriverAbsent => StatusCodes.Status409Conflict,
        PlanningRefusalCode.ShiftInvalid or PlanningRefusalCode.PeriodInvalid or PlanningRefusalCode.RemarkTooLong => StatusCodes.Status400BadRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The API doesn't know this refusal's status."),
    };
}
