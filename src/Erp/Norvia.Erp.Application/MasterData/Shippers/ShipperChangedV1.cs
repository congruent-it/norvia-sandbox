using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Shippers.List;

namespace Norvia.Erp.Application.MasterData.Shippers;

/// <summary>
/// The Canonical Model event <c>Shipper</c>, version 1 (contracts/canonical-model/shipper.v1.schema.json): the whole Shipper as it is
/// after an edit, with its Master-data revision. The TMS replaces its read copy with it (E3).
/// </summary>
public sealed record ShipperChangedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, ShipperView Shipper)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipperChanged";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.MasterDataEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.MasterData),
        new(TrackingKeys.PartyId, Shipper.Id),
    ];
}
