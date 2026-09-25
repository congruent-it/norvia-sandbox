using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings;

/// <summary>The Norvia Partner API, the only way the Partner portal reaches Norvia (ADR 0001).</summary>
public interface INorviaPartnerApi
{
    /// <summary>
    /// Sends a booking as the Persona, with its own token and subscription key. Norvia unreachable is an answer, not an
    /// error: <see cref="NorviaAnswer.NotReceived"/>.
    /// </summary>
    Task<NorviaAnswer> SubmitAsync(Persona persona, BookingRequest booking, CancellationToken cancellationToken);
}
