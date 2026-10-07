using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.ShipperNotifications.Receive;

/// <summary>Puts a notification Norvia sent a Shipper in the Shipper's inbox, unless the inbox has it already.</summary>
/// <param name="PartyId">The Shipper's ID at Norvia, such as <c>SHP-0001</c>, which names the Persona whose inbox it is.</param>
public sealed record ReceiveShipperNotificationCommand(
    string PartyId,
    Guid NotificationId,
    string CorrelationId,
    ShipperNotificationContent Content) : ICommand<Result<ReceiveShipperNotificationResult>>;

/// <param name="Repeated">True when the inbox had the notification already, so nothing was added.</param>
public sealed record ReceiveShipperNotificationResult(Guid NotificationId, bool Repeated);
