using Ardalis.Specification;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The open booking requests whose automatic answer is due, the earliest first, read untracked: answering each one is its own use case.</summary>
public sealed class CarrierBookingRequestsDueForAutoAnswerSpec : Specification<CarrierBookingRequest>
{
    public CarrierBookingRequestsDueForAutoAnswerSpec(DateTimeOffset now)
    {
        Query.Where(request => request.Status == CarrierBookingRequestStatus.Open && request.AutoAnswerDueAt != null && request.AutoAnswerDueAt <= now)
            .OrderBy(request => request.AutoAnswerDueAt)
            .AsNoTracking();
    }
}
