using Ardalis.Specification;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The Confirmed booking requests whose next automatic Tracking event is due, the earliest first, read untracked: sending each one is its own use case.</summary>
public sealed class CarrierBookingRequestsDueForAutoProgressSpec : Specification<CarrierBookingRequest>
{
    public CarrierBookingRequestsDueForAutoProgressSpec(DateTimeOffset now)
    {
        Query.Where(request => request.Status == CarrierBookingRequestStatus.Confirmed && request.AutoProgressDueAt != null && request.AutoProgressDueAt <= now)
            .OrderBy(request => request.AutoProgressDueAt)
            .AsNoTracking();
    }
}
