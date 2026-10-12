using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.GetByNumber;

/// <summary>A Stop, derived from its Shipment: the collection or the delivery, where, when and what goods.</summary>
/// <param name="Outcome">What the Driver reported for the Stop (P12); Open until they have.</param>
/// <param name="OutcomeAt">When it happened, as the Driver reported it; null while the Stop is Open.</param>
/// <param name="OutcomeRemark">The Driver's remark with the outcome, if they gave one.</param>
/// <param name="Name">Who receives the goods at a delivery Stop; null at a collection Stop or when the Shipper didn't say.</param>
public sealed record TripStopView(
    int Position,
    StopKind Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string Place,
    string? Name,
    DateTimeOffset? WindowEarliest,
    DateTimeOffset? WindowLatest,
    string Goods,
    StopOutcomeKind Outcome = StopOutcomeKind.Open,
    DateTimeOffset? OutcomeAt = null,
    string? OutcomeRemark = null);
