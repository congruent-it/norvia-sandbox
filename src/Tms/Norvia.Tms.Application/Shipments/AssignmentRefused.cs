using Ardalis.Result;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <summary>
/// An assignment or reassignment the TMS refused, as a result: Invalid, with one error carrying the refusal's stable
/// code and its message, so the API answers with the code and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class AssignmentRefused
{
    public static Result<TResult> Because<TResult>(AssignmentRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("Assignment", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
