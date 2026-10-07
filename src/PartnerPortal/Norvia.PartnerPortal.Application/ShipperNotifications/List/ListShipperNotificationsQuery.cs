using Mediator;

namespace Norvia.PartnerPortal.Application.ShipperNotifications.List;

/// <summary>The notifications in a Shipper Persona's inbox, the latest first.</summary>
public sealed record ListShipperNotificationsQuery(string PersonaId) : IQuery<IReadOnlyList<ShipperNotificationView>>;
