using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>
/// A notification Norvia sent a Shipper Persona about an Exception, in the Shipper's inbox. The notification ID is its
/// key: the same notification received again is the one the inbox already has.
/// </summary>
public sealed class ShipperNotification : EntityBase<ShipperNotificationId>, IAggregateRoot
{
    // For EF Core, which sets the content from its columns.
    private ShipperNotification()
    {
        PersonaId = null!;
        CorrelationId = null!;
        Content = null!;
    }

    private ShipperNotification(
        ShipperNotificationId id, PersonaId personaId, Guid notificationId, string correlationId, DateTimeOffset receivedAt, ShipperNotificationContent content)
    {
        Id = id;
        PersonaId = personaId;
        NotificationId = notificationId;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
        Content = content;
    }

    public PersonaId PersonaId { get; private set; }

    /// <summary>Norvia's ID of the notification, the key of the inbox.</summary>
    public Guid NotificationId { get; private set; }

    /// <summary>The correlation ID the notification arrived with.</summary>
    public string CorrelationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public ShipperNotificationContent Content { get; private set; }

    public static ShipperNotification Receive(
        PersonaId personaId, Guid notificationId, string correlationId, DateTimeOffset receivedAt, ShipperNotificationContent content)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentOutOfRangeException.ThrowIfEqual(notificationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(content);
        return new ShipperNotification(ShipperNotificationId.New(), personaId, notificationId, correlationId, receivedAt, content);
    }
}
