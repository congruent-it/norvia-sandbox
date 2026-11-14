using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Vehicles;

namespace Norvia.Erp.Application.MasterData.Vehicles;

/// <summary>
/// The Canonical Model event <c>Vehicle</c>, version 1 (contracts/canonical-model/vehicle.v1.schema.json): the whole Vehicle as it is
/// after an edit, with its Master-data revision. The TMS replaces its read copy with it (E3).
/// </summary>
public sealed record VehicleV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, VehicleView Vehicle)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "Vehicle";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.MasterDataEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.MasterData),
        new(TrackingKeys.PartyId, Vehicle.Id),
    ];
}
