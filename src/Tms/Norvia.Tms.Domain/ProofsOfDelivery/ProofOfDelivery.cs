using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>
/// The Consignee's confirmation that it received a Shipment's goods (P6): who signed, when and where, in which condition,
/// backed by files. A Shipment has at most one current Proof of delivery. The TMS owns it, and it never changes the
/// Shipment's status: only Tracking events do. It is made only by <see cref="Record"/>.
/// </summary>
public sealed class ProofOfDelivery : EntityBase<ProofOfDeliveryId>, IAggregateRoot
{
    private ProofOfDelivery(
        ProofOfDeliveryId id,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        ProofOfDeliverySource source,
        string documentId,
        string carrierBookingReference,
        string receivedBy,
        DateTimeOffset deliveryTime,
        DeliveryCondition condition,
        string? remark,
        IReadOnlyList<ProofOfDeliveryFile> files,
        CorrelationId correlationId,
        DateTimeOffset receivedAt)
    {
        Id = id;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        Source = source;
        DocumentId = documentId;
        CarrierBookingReference = carrierBookingReference;
        ReceivedBy = receivedBy;
        DeliveryTime = deliveryTime;
        Condition = condition;
        Remark = remark;
        Files = files;
        CorrelationId = correlationId;
        ReceivedAt = receivedAt;
    }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public ProofOfDeliverySource Source { get; private set; }

    /// <summary>The sender's own ID for the Proof of delivery, unique per source.</summary>
    public string DocumentId { get; private set; }

    /// <summary>The Carrier's own reference for the booking the Proof of delivery was matched by.</summary>
    public string CarrierBookingReference { get; private set; }

    /// <summary>The name of the person who signed.</summary>
    public string ReceivedBy { get; private set; }

    /// <summary>When the goods were delivered, with the offset of where.</summary>
    public DateTimeOffset DeliveryTime { get; private set; }

    public TrackingLocation Location { get; private set; } = null!; // set when made, and a complex property, which a constructor can't bind

    public DeliveryCondition Condition { get; private set; }

    public string? Remark { get; private set; }

    public IReadOnlyList<ProofOfDeliveryFile> Files { get; private set; }

    /// <summary>The Message trail of the call that sent it, which API Management creates.</summary>
    public CorrelationId CorrelationId { get; private set; }

    /// <summary>When the TMS recorded it, apart from when the goods were delivered.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Whether it is the Shipment's current Proof of delivery, as opposed to one a later one replaced.</summary>
    public bool IsCurrent { get; private set; } = true;

    /// <summary>
    /// Records a Proof of delivery as the Shipment's current one and raises ProofOfDeliveryReceived. The Shipment's status
    /// stays as it is. A current one it replaces is superseded, and nothing of it is merged into the new one.
    /// </summary>
    public static ProofOfDelivery Record(
        Shipment shipment, ProofOfDeliveryReport report, CorrelationId correlationId, DateTimeOffset receivedAt, ProofOfDelivery? replaces = null)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(report.Source);
        ArgumentNullException.ThrowIfNull(report.Location);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.DocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.CarrierBookingReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.ReceivedBy);
        if (report.Files.Count == 0)
        {
            throw new ArgumentException("A Proof of delivery needs at least one file.", nameof(report));
        }

        if (replaces is not null && (replaces.ShipmentId != shipment.Id || !replaces.IsCurrent))
        {
            throw new ArgumentException("Only the Shipment's current Proof of delivery can be replaced.", nameof(replaces));
        }

        var proofOfDelivery = new ProofOfDelivery(
            ProofOfDeliveryId.New(),
            shipment.Id,
            shipment.Number,
            report.Source,
            report.DocumentId,
            report.CarrierBookingReference,
            report.ReceivedBy,
            report.DeliveryTime,
            report.Condition,
            report.Remark,
            [.. report.Files],
            correlationId,
            receivedAt)
        {
            Location = report.Location,
        };
        replaces?.IsCurrent = false;
        proofOfDelivery.RegisterDomainEvent(new ProofOfDeliveryReceivedEvent(shipment, proofOfDelivery, replacesEarlier: replaces is not null));
        return proofOfDelivery;
    }
}
