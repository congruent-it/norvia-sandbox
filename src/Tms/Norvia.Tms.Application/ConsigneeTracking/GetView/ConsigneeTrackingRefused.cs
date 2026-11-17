using Ardalis.Result;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ConsigneeTracking.GetView;

internal static class ConsigneeTrackingRefused
{
    public static Result<TResult> NotFound<TResult>() =>
        Result<TResult>.Invalid(new ValidationError(
            "ConsigneeTracking", "The tracking code is not known to the TMS.", ConsigneeTrackingRefusalCode.TrackingCodeNotFound.ToString(), ValidationSeverity.Error));
}
