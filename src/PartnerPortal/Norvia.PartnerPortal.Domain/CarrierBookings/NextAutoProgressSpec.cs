using Ardalis.Specification;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The Confirmed booking request whose automatic Tracking event comes first.</summary>
public sealed class NextAutoProgressSpec : Specification<CarrierBookingRequest>
{
    public NextAutoProgressSpec()
    {
        Query.Where(request => request.Status == CarrierBookingRequestStatus.Confirmed && request.AutoProgressDueAt != null)
            .OrderBy(request => request.AutoProgressDueAt)
            .Take(1)
            .AsNoTracking();
    }
}
