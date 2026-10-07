using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>The notifications in a Shipper Persona's inbox, the latest first.</summary>
public sealed class ShipperNotificationsOfPersonaSpec : Specification<ShipperNotification>
{
    public ShipperNotificationsOfPersonaSpec(PersonaId personaId)
    {
        Query.Where(notification => notification.PersonaId == personaId)
            .OrderByDescending(notification => notification.ReceivedAt)
            .AsNoTracking();
    }
}
