using Ardalis.Result;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents;

/// <summary>
/// A Tracking event the TMS refused, as a result: Invalid, with one error carrying the refusal's message and its stable
/// code, so the API answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class TrackingEventRefused
{
    public static Result<TResult> Because<TResult>(TrackingEventRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("TrackingEvent", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
