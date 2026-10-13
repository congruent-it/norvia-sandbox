using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Norvia Partner API, the only door the Simulator books through (ADR 0001). A booking is sent as its Shipper's Persona,
/// with the Persona's token and subscription key. Norvia unreachable is an answer, not an error:
/// <see cref="Domain.Log.LogOutcome.NotReceived"/>.
/// </summary>
public interface IPartnerApi
{
    Task<BookingAnswer> SendBookingAsync(PlannedBooking booking, CancellationToken cancellationToken);
}
