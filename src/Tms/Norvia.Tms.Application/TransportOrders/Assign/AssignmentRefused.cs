using Ardalis.Result;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// An assignment the TMS refused, as a result: Invalid, with one error carrying the refusal's stable code and its
/// message, so the API answers with the code and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class AssignmentRefused
{
    public static Result<AssignTransportOrderResult> Because(AssignmentRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result.Invalid(new ValidationError(nameof(AssignTransportOrderCommand), refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
