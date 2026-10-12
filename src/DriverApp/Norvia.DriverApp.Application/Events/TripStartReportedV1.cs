namespace Norvia.DriverApp.Application.Events;

/// <summary>
/// The Canonical Model event <c>TripStartReported</c>, version 1 (contracts/canonical-model/trip-start-reported.v1.schema.json). The Driver
/// pressed Start Trip on a Dispatched Trip. The Driver app reports and the TMS decides, so it says what the Driver did, not that the Trip is In progress.
/// </summary>
public sealed record TripStartReportedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, string TripNumber, int Version, string DriverId)
    : DriverAppEvent(EventId, OccurredAt, CorrelationId, TripNumber, Version, DriverId)
{
    public override string EventType => "TripStartReported";

    public override int EventVersion => 1;
}
