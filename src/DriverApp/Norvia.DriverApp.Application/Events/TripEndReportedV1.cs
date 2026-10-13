namespace Norvia.DriverApp.Application.Events;

/// <summary>
/// The Canonical Model event <c>TripEndReported</c>, version 1 (contracts/canonical-model/trip-end-reported.v1.schema.json). The Driver pressed End Trip on an
/// In progress Trip whose Stops all have an outcome. The Driver app reports and the TMS decides, so it says what the Driver did, not that the Trip is Completed.
/// </summary>
public sealed record TripEndReportedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, string TripNumber, int Version, string DriverId)
    : DriverAppEvent(EventId, OccurredAt, CorrelationId, TripNumber, Version, DriverId)
{
    public override string EventType => "TripEndReported";

    public override int EventVersion => 1;
}
