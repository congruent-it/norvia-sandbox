using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.ShipperNotifications.Receive;

public sealed class ReceiveShipperNotificationHandler(IRepository<ShipperNotification> repository, IPersonaDirectory personas, TimeProvider time)
    : ICommandHandler<ReceiveShipperNotificationCommand, Result<ReceiveShipperNotificationResult>>
{
    public async ValueTask<Result<ReceiveShipperNotificationResult>> Handle(ReceiveShipperNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipper = personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Shipper && persona.PartyId == command.PartyId);
        if (shipper is null)
        {
            return Result.NotFound($"There is no Shipper Persona for '{command.PartyId}'.");
        }

        // The notification ID is the key: a repeated notification is the one the inbox has, and is not an error.
        if (await repository.AnyAsync(new ShipperNotificationByNotificationIdSpec(shipper.Id, command.NotificationId), cancellationToken))
        {
            return new ReceiveShipperNotificationResult(command.NotificationId, Repeated: true);
        }

        await repository.AddAsync(
            ShipperNotification.Receive(shipper.Id, command.NotificationId, command.CorrelationId, time.GetUtcNow(), command.Content), cancellationToken);
        return new ReceiveShipperNotificationResult(command.NotificationId, Repeated: false);
    }
}
