using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperNotifications.List;

/// <summary>The notifications in a Shipper Persona's inbox, the latest first.</summary>
public sealed record ListShipperNotificationsQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<ShipperNotificationView>>;
