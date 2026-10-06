using Ardalis.Result;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions;

/// <summary>
/// A Planner's action on an Exception the TMS refused, as a result: Invalid, with one error carrying the refusal's message
/// and its stable code, so the API answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class ExceptionRefused
{
    public static Result<TResult> Because<TResult>(ExceptionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("Exception", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
