using Ardalis.Result;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing;

/// <summary>
/// Customer service's action on a Transport Order's billing that the TMS refused, as a result: Invalid, with one error carrying
/// the refusal's message and its stable code, so the API answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class BillingRefused
{
    public static Result<TResult> Because<TResult>(BillingRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("TransportOrder", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
