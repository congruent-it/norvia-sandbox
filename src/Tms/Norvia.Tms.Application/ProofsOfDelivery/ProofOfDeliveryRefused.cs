using Ardalis.Result;
using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Application.ProofsOfDelivery;

/// <summary>
/// A Proof of delivery the TMS refused, as a result: Invalid, with one error carrying the refusal's message and its stable
/// code, so the API answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class ProofOfDeliveryRefused
{
    public static Result<TResult> Because<TResult>(ProofOfDeliveryRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("ProofOfDelivery", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
