using Ardalis.Specification;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The open booking request whose automatic answer comes first.</summary>
public sealed class NextAutoAnswerSpec : Specification<CarrierBookingRequest>
{
    public NextAutoAnswerSpec()
    {
        Query.Where(request => request.Status == CarrierBookingRequestStatus.Open && request.AutoAnswerDueAt != null)
            .OrderBy(request => request.AutoAnswerDueAt)
            .Take(1)
            .AsNoTracking();
    }
}
