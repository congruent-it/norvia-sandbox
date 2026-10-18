using Ardalis.Result;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Application.BillableLines;

/// <summary>
/// A recording the ERP refused, as a result: Invalid, with one error carrying the refusal's message and its stable code, so the API
/// answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class BillableLineRefused
{
    public static Result<TResult> Because<TResult>(BillableLineRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("BillableLine", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
