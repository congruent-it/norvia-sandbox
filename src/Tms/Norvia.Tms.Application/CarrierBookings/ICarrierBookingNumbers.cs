using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <summary>Hands out Carrier booking numbers, each only once.</summary>
public interface ICarrierBookingNumbers
{
    Task<CarrierBookingNumber> NextAsync(CancellationToken cancellationToken);
}
