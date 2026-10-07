using Ardalis.Result;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ShipperTracking;

/// <summary>
/// A Shipper's tracking query the TMS refused, as a result: Invalid, with one error carrying the refusal's message and its stable
/// code, so the API answers with it.
/// </summary>
public static class ShipperTrackingRefused
{
    public static Result<TResult> Because<TResult>(ShipperTrackingRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("ShipperTracking", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
