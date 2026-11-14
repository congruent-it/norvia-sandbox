using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Drivers;

namespace Norvia.Erp.Application.MasterData.Drivers;

/// <summary>
/// The Canonical Model event <c>Driver</c>, version 1 (contracts/canonical-model/driver.v1.schema.json): the whole Driver as it is
/// after an edit, with its Master-data revision. The TMS replaces its read copy with it (E3).
/// </summary>
public sealed record DriverChangedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, DriverView Driver)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "DriverChanged";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.MasterDataEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.MasterData),
        new(TrackingKeys.PartyId, Driver.Id),
    ];
}
