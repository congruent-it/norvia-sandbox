using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>The notification a Shipper Persona already has for a notification ID.</summary>
public sealed class ShipperNotificationByNotificationIdSpec : Specification<ShipperNotification>
{
    public ShipperNotificationByNotificationIdSpec(PersonaId personaId, Guid notificationId)
    {
        Query.Where(notification => notification.PersonaId == personaId && notification.NotificationId == notificationId)
            .AsNoTracking();
    }
}
