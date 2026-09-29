using Ardalis.Result;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <summary>
/// A Carrier booking operation the TMS refused, as a result: Invalid, with one error carrying the refusal's message and,
/// where the refusal has one, its stable code, so the API answers with it and the trace records it as the
/// <c>failureCode</c>.
/// </summary>
public static class CarrierBookingRefused
{
    public static Result<TResult> Because<TResult>(CarrierBookingRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("CarrierBookingAnswer", refusal.Message, refusal.Code?.ToString() ?? string.Empty, ValidationSeverity.Error));
    }
}
