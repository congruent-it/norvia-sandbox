namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>What a Driver report asks of the TMS, as the Logic App's route by <c>eventType</c> does.</summary>
internal enum DriverReportKind
{
    TripStart,
    TripEnd,
    Stop,
    ProofOfDelivery,
}
