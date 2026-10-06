using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>
/// A message the Planner sends the Shipper about an open Exception (P7). It is one-way: the Shipper can't reply through the TMS.
/// It starts Requested, and the Shipper notification Logic App reports it Sent or Not delivered, which is final. A Planner can
/// notify as often as needed, so an Exception has any number of them.
/// </summary>
public sealed class ShipperNotification : EntityBase<ShipperNotificationId>, IAggregateRoot
{
    public const int MaxMessageLength = 2000;

    public const int MaxPlannerLength = 200;

    public const int MaxCauseLength = 1000;

    private ShipperNotification(
        ShipperNotificationId id,
        ExceptionId exceptionId,
        ExceptionNumber exceptionNumber,
        ShipmentNumber shipmentNumber,
        TransportOrderNumber transportOrderNumber,
        ShipperId shipperId,
        string? shipperReference,
        ExceptionType type,
        string message,
        string planner,
        CorrelationId correlationId,
        DateTimeOffset requestedAt)
    {
        Id = id;
        ExceptionId = exceptionId;
        ExceptionNumber = exceptionNumber;
        ShipmentNumber = shipmentNumber;
        TransportOrderNumber = transportOrderNumber;
        ShipperId = shipperId;
        ShipperReference = shipperReference;
        Type = type;
        Message = message;
        Planner = planner;
        CorrelationId = correlationId;
        RequestedAt = requestedAt;
    }

    public ExceptionId ExceptionId { get; private set; }

    public ExceptionNumber ExceptionNumber { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public TransportOrderNumber TransportOrderNumber { get; private set; }

    public ShipperId ShipperId { get; private set; }

    /// <summary>The Shipper's own reference of the Transport Order, so the Shipper recognises the Shipment.</summary>
    public string? ShipperReference { get; private set; }

    public ExceptionType Type { get; private set; }

    /// <summary>What the Planner wrote, starting from the template for the Exception's type.</summary>
    public string Message { get; private set; }

    /// <summary>The ETA of the Shipment when the Planner notified, when it has one.</summary>
    public DateTimeOffset? Eta { get; private set; }

    /// <summary>The earliest time of the delivery window the Planner agreed for the Shipment, when there is one.</summary>
    public DateTimeOffset? AgreedDeliveryEarliest { get; private set; }

    /// <summary>The latest time of the delivery window the Planner agreed for the Shipment, when there is one.</summary>
    public DateTimeOffset? AgreedDeliveryLatest { get; private set; }

    public string Planner { get; private set; }

    public ShipperNotificationStatus Status { get; private set; } = ShipperNotificationStatus.Requested;

    /// <summary>The Message trail of the Planner's action, which the notification and the Logic App's report continue.</summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? NotDeliveredAt { get; private set; }

    /// <summary>The endpoint's HTTP status code, or the technical failure's own code, of a Not delivered notification.</summary>
    public int? NotDeliveredStatusCode { get; private set; }

    /// <summary>The technical cause of a Not delivered notification, in plain language.</summary>
    public string? NotDeliveredMessage { get; private set; }

    /// <summary>Creates a Requested Shipper notification for the Exception and raises ShipperNotificationRequested. The text and the Planner are checked at the boundary first.</summary>
    public static ShipperNotification Request(
        ExceptionCase exception,
        string? shipperReference,
        string message,
        string planner,
        DateTimeOffset? eta,
        TimeWindow? agreedDeliveryWindow,
        CorrelationId correlationId,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(planner);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Length, MaxMessageLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(planner.Length, MaxPlannerLength);
        var notification = new ShipperNotification(
            ShipperNotificationId.New(),
            exception.Id,
            exception.Number,
            exception.ShipmentNumber,
            exception.TransportOrderNumber,
            exception.ShipperId,
            shipperReference,
            exception.Type,
            message,
            planner,
            correlationId,
            requestedAt)
        {
            Eta = eta,
            AgreedDeliveryEarliest = agreedDeliveryWindow?.Earliest,
            AgreedDeliveryLatest = agreedDeliveryWindow?.Latest,
        };
        notification.RegisterDomainEvent(new ShipperNotificationRequestedEvent(notification));
        return notification;
    }

    /// <summary>Records the Shipper's endpoint acknowledged the notification. Returns false, changing nothing, for one that is no longer Requested.</summary>
    public bool ReportSent(DateTimeOffset sentAt)
    {
        if (Status != ShipperNotificationStatus.Requested)
        {
            return false;
        }

        Status = ShipperNotificationStatus.Sent;
        SentAt = sentAt;
        return true;
    }

    /// <summary>
    /// Records the notification never reached the Shipper, with the technical cause, and raises ShipperNotificationNotDelivered.
    /// Returns false, changing nothing, for one that is no longer Requested.
    /// </summary>
    public bool ReportNotDelivered(int statusCode, string message, DateTimeOffset notDeliveredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (Status != ShipperNotificationStatus.Requested)
        {
            return false;
        }

        Status = ShipperNotificationStatus.NotDelivered;
        NotDeliveredAt = notDeliveredAt;
        NotDeliveredStatusCode = statusCode;
        NotDeliveredMessage = message.Length > MaxCauseLength ? message[..MaxCauseLength] : message;
        RegisterDomainEvent(new ShipperNotificationNotDeliveredEvent(this));
        return true;
    }
}
