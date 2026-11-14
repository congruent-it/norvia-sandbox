using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.MasterData.RateCards;

/// <summary>
/// The Canonical Model event <c>CarrierRateCard</c>, version 1 (contracts/canonical-model/carrier-rate-card.v1.schema.json): the whole
/// Carrier Rate Card with its full list of dated versions, as it is after an edit. The TMS replaces its list with it (E3).
/// </summary>
public sealed record CarrierRateCardChangedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, CarrierRateCardRecord CarrierRateCard)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierRateCardChanged";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.MasterDataEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.MasterData),
        new(TrackingKeys.PartyId, CarrierRateCard.CarrierId),
    ];
}
