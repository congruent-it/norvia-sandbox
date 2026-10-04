using Ardalis.Result;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Shipments;

/// <summary>What the Planner's assignment, reassignment and release calls share: checking the call, and answering a refusal.</summary>
internal static class AssignmentCalls
{
    /// <summary>The call's errors: an idempotency key, and a Carrier named exactly when the call assigns to one.</summary>
    public static Dictionary<string, string[]> Check(PlanningAction action, string? idempotencyKey, AssignedTo assignedTo, string? carrierId)
    {
        var errors = Check(action, idempotencyKey);
        if (assignedTo == AssignedTo.Carrier && string.IsNullOrWhiteSpace(carrierId))
        {
            errors["CarrierId"] = [$"{action.AsSubject()} to a Carrier names the Carrier."];
        }
        else if (assignedTo == AssignedTo.OwnFleet && carrierId is not null)
        {
            errors["CarrierId"] = [$"{action.AsSubject()} to Own fleet names no Carrier."];
        }

        return errors;
    }

    /// <summary>The call's errors: an idempotency key, the same for each retry.</summary>
    public static Dictionary<string, string[]> Check(PlanningAction action, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength)
        {
            errors[TransportOrderEndpoints.IdempotencyKeyHeader] =
                [$"{action.AsSubject()} needs an {TransportOrderEndpoints.IdempotencyKeyHeader} of 1 to {IdempotencyKey.MaxLength} characters, the same for each retry."];
        }

        return errors;
    }

    /// <summary>The problem for a refused planning call.</summary>
    public static ProblemHttpResult Refused<T>(Result<T> result, string title, string correlationId) =>
        RefusalProblem.Of<AssignmentRefusalCode, T>(result, title, correlationId, StatusCodeOf);

    private static int StatusCodeOf(AssignmentRefusalCode code) => code switch
    {
        AssignmentRefusalCode.TransportOrderNotFound or AssignmentRefusalCode.ShipmentNotFound => StatusCodes.Status404NotFound,
        AssignmentRefusalCode.UnknownCarrier or AssignmentRefusalCode.InactiveCarrier => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict,
    };
}
