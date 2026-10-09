using Ardalis.Result;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>
/// A Planner's action on a day of the Planning horizon that the TMS refused, as a result: Invalid, with one error carrying the
/// refusal's message and its stable code.
/// </summary>
public static class PlanningRefused
{
    public static Result<TResult> Because<TResult>(PlanningRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("PlanningDay", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }

    public static Result Because(PlanningRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result.Invalid(new ValidationError("PlanningDay", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
