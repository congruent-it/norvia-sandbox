using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Carriers.List;

namespace Norvia.Erp.Application.MasterData.Carriers;

/// <summary>
/// The Canonical Model event <c>Carrier</c>, version 1 (contracts/canonical-model/carrier.v1.schema.json): the whole Carrier as it is
/// after an edit, with its Master-data revision. The TMS replaces its read copy with it (E3).
/// </summary>
public sealed record CarrierV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, CarrierView Carrier)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "Carrier";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.MasterDataEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.MasterData),
        new(TrackingKeys.PartyId, Carrier.Id),
    ];
}
