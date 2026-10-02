using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>Words for how a request in the Carrier's inbox stands, and for what Norvia replied to an answer.</summary>
public static class RequestStatusText
{
    public static string Reason(CarrierRejectionReason reason) => reason switch
    {
        CarrierRejectionReason.NoCapacity => "No capacity",
        CarrierRejectionReason.LaneNotServed => "Lane not served",
        CarrierRejectionReason.WindowNotFeasible => "Window not feasible",
        CarrierRejectionReason.GoodsNotAccepted => "Goods not accepted",
        CarrierRejectionReason.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Every reason has words."),
    };

    /// <summary>The state of an answered or closed request on one line; "Open" while nobody has answered.</summary>
    public static string Of(CarrierBookingRequestView request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Status switch
        {
            CarrierBookingRequestStatus.Open => "Open",
            CarrierBookingRequestStatus.Confirmed => Confirmed(request),
            CarrierBookingRequestStatus.Rejected => Rejected(request),
            CarrierBookingRequestStatus.WithdrawnByNorvia => "Withdrawn by Norvia",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Status, "Every status has words."),
        };
    }

    private static string Confirmed(CarrierBookingRequestView request)
    {
        var text = $"Confirmed, your reference {request.CarrierBookingReference}";
        return request.PlannedCollection is { } planned ? $"{text}, collecting {PortalTime.Format(planned)}" : text;
    }

    private static string Rejected(CarrierBookingRequestView request)
    {
        var reason = request.RejectionReason is { } code ? Reason(code) : "No reason";
        return string.IsNullOrWhiteSpace(request.RejectionText) ? $"Rejected: {reason}" : $"Rejected: {reason}, {request.RejectionText}";
    }
}
