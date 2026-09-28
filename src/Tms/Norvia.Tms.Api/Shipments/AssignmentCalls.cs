using Ardalis.Result;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Shipments;

/// <summary>What the Planner's assignment and reassignment calls share: checking the call, and answering a refusal.</summary>
internal static class AssignmentCalls
{
    /// <summary>The call's errors: an idempotency key, and a Carrier named exactly when the call assigns to one.</summary>
    /// <param name="action">What the call does, such as "An assignment", for the messages.</param>
    public static Dictionary<string, string[]> Check(string action, string? idempotencyKey, AssignedTo assignedTo, string? carrierId)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength)
        {
            errors[TransportOrderEndpoints.IdempotencyKeyHeader] =
                [$"{action} needs an {TransportOrderEndpoints.IdempotencyKeyHeader} of 1 to {IdempotencyKey.MaxLength} characters, the same for each retry."];
        }

        if (assignedTo == AssignedTo.Carrier && string.IsNullOrWhiteSpace(carrierId))
        {
            errors["CarrierId"] = [$"{action} to a Carrier names the Carrier."];
        }
        else if (assignedTo == AssignedTo.OwnFleet && carrierId is not null)
        {
            errors["CarrierId"] = [$"{action} to Own fleet names no Carrier."];
        }

        return errors;
    }

    /// <summary>The problem for a refused call, with the refusal's code, and a status that says which rule it broke.</summary>
    public static ProblemHttpResult Refused<T>(Result<T> result, string title)
    {
        var refusal = result.ValidationErrors.First();
        return TypedResults.Problem(
            statusCode: StatusCodeOf(Enum.Parse<AssignmentRefusalCode>(refusal.ErrorCode)),
            title: title,
            detail: refusal.ErrorMessage,
            extensions: new Dictionary<string, object?> { ["code"] = refusal.ErrorCode });
    }

    private static int StatusCodeOf(AssignmentRefusalCode code) => code switch
    {
        AssignmentRefusalCode.TransportOrderNotFound or AssignmentRefusalCode.ShipmentNotFound => StatusCodes.Status404NotFound,
        AssignmentRefusalCode.UnknownCarrier or AssignmentRefusalCode.InactiveCarrier => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict,
    };
}
