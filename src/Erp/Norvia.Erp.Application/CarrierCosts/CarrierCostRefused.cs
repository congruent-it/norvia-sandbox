using Ardalis.Result;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Application.CarrierCosts;

/// <summary>
/// A recording the ERP refused, as a result: Invalid, with one error carrying the refusal's message and its stable code, so the API
/// answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class CarrierCostRefused
{
    public static Result<TResult> Because<TResult>(CarrierCostRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("CarrierCost", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
