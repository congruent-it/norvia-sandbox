using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// Something that went wrong with one Shipment on its way and needs a Planner's decision (P7). It has an Exception number
/// the TMS gives it, a type, one or more causes and, once resolved, an outcome and a remark. A Shipment has at most one
/// open Exception of each type: a later cause of that type joins it. The TMS never closes it by itself. It is made only by
/// the Tracking event that causes it, in the same transaction, and never deleted.
/// </summary>
public sealed class ExceptionCase : EntityBase<ExceptionId>, IAggregateRoot
{
    private ExceptionCase(
        ExceptionId id,
        ExceptionNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        TransportOrderNumber transportOrderNumber,
        ShipperId shipperId,
        CarrierId? carrierId,
        ExceptionType type,
        bool afterDelivered,
        CorrelationId correlationId,
        DateTimeOffset openedAt)
    {
        Id = id;
        Number = number;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        TransportOrderNumber = transportOrderNumber;
        ShipperId = shipperId;
        CarrierId = carrierId;
        Type = type;
        AfterDelivered = afterDelivered;
        CorrelationId = correlationId;
        OpenedAt = openedAt;
    }

    public ExceptionNumber Number { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public TransportOrderNumber TransportOrderNumber { get; private set; }

    public ShipperId ShipperId { get; private set; }

    /// <summary>The Carrier that carried the Shipment when the Exception opened; null for Own fleet.</summary>
    public CarrierId? CarrierId { get; private set; }

    /// <summary>Who carried the Shipment when the Exception opened.</summary>
    public CarriedBy CarriedBy => CarrierId is { } carrierId ? CarriedBy.Carrier(carrierId) : CarriedBy.OwnFleet;

    public ExceptionType Type { get; private set; }

    public ExceptionStatus Status { get; private set; } = ExceptionStatus.Open;

    /// <summary>
    /// Whether the Shipment was Delivered already when the Tracking event that opened the Exception arrived: P7 never moves a
    /// Shipment's status, so the Shipment stays Delivered.
    /// </summary>
    public bool AfterDelivered { get; private set; }

    /// <summary>The Message trail of the cause that opened the Exception.</summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>The causes, in the order they came: the one that opened the Exception first.</summary>
    public IReadOnlyList<ExceptionCause> Causes { get; private set; } = [];

    public ExceptionOutcome? Outcome { get; private set; }

    public string? Remark { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>The Message trail of the Planner's resolution, which starts in the TMS UI and so gets a trail of its own.</summary>
    public CorrelationId? ResolutionCorrelationId { get; private set; }

    /// <summary>
    /// Opens the Exception a Tracking event of the Shipment causes, with the event as its first cause and its correlation ID,
    /// and raises ExceptionOpened.
    /// </summary>
    public static ExceptionCase Open(ExceptionNumber number, Shipment shipment, TrackingEvent cause, DateTimeOffset openedAt)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(cause);
        var type = ExceptionTypes.OpenedBy(cause.Type)
            ?? throw new ArgumentException(Invariant($"A {cause.Type} Tracking event opens no Exception."), nameof(cause));
        var exception = new ExceptionCase(
            ExceptionId.New(),
            number,
            shipment.Id,
            shipment.Number,
            shipment.TransportOrderNumber,
            shipment.ShipperId,
            shipment.CarriedBy.CarrierId,
            type,
            shipment.Status == ShipmentStatus.Delivered,
            cause.CorrelationId,
            openedAt)
        {
            Causes = [CauseOf(cause, openedAt)],
        };
        exception.RegisterDomainEvent(new ExceptionOpenedEvent(exception, cause));
        return exception;
    }

    /// <summary>Adds a later Tracking event of the Exception's type as a cause. It raises nothing.</summary>
    public void AddCause(TrackingEvent cause, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(cause);
        if (ExceptionTypes.OpenedBy(cause.Type) != Type)
        {
            throw new ArgumentException(Invariant($"A {cause.Type} Tracking event is no cause of a {Type} Exception."), nameof(cause));
        }

        Causes = [.. Causes, CauseOf(cause, addedAt)];
    }

    /// <summary>
    /// Resolves an open Exception with the Planner's outcome and remark, and raises ExceptionResolved. Returns the refusal,
    /// changing nothing, for an Exception that isn't open. The outcome and the remark are checked at the boundary first.
    /// </summary>
    public ExceptionRefusal? Resolve(ExceptionOutcome outcome, string remark, CorrelationId correlationId, DateTimeOffset resolvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remark);
        ArgumentNullException.ThrowIfNull(correlationId);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome isn't one of the TMS's.");
        }

        if (Status != ExceptionStatus.Open)
        {
            return new(ExceptionRefusalCode.ExceptionNotOpen, Invariant($"Exception {Number} is {Status}, not Open."));
        }

        Status = ExceptionStatus.Resolved;
        Outcome = outcome;
        Remark = remark;
        ResolvedAt = resolvedAt;
        ResolutionCorrelationId = correlationId;
        RegisterDomainEvent(new ExceptionResolvedEvent(this));
        return null;
    }

    private static ExceptionCause CauseOf(TrackingEvent trackingEvent, DateTimeOffset addedAt) =>
        new(trackingEvent.Id, trackingEvent.SourceEventId, trackingEvent.CorrelationId, addedAt);
}
