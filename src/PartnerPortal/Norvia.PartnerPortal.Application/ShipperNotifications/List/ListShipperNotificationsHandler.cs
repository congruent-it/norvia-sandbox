using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.ShipperNotifications.List;

public sealed class ListShipperNotificationsHandler(IReadRepository<ShipperNotification> repository)
    : IQueryHandler<ListShipperNotificationsQuery, IReadOnlyList<ShipperNotificationView>>
{
    public async ValueTask<IReadOnlyList<ShipperNotificationView>> Handle(ListShipperNotificationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notifications = await repository.ListAsync(new ShipperNotificationsOfPersonaSpec(new PersonaId(query.PersonaId)), cancellationToken);
        return [.. notifications.Select(ShipperNotificationView.From)];
    }
}
