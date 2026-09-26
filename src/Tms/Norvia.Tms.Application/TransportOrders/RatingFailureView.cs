using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <summary>Why Rating failed, as the query API and the Canonical Model show it.</summary>
/// <param name="Code">The stable failure code, such as <c>NoMatchingLane</c>.</param>
public sealed record RatingFailureView(string Code, string Message)
{
    public static RatingFailureView? From(RatingFailure? failure) =>
        failure is null ? null : new(failure.Code.ToString(), failure.Message);
}
