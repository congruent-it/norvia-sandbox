using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.List;

/// <summary>The booking requests in a Carrier Persona's inbox, the latest first.</summary>
public sealed record ListCarrierBookingRequestsQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<CarrierBookingRequestView>>;
