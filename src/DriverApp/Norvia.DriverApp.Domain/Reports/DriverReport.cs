using Ardalis.SharedKernel;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// One thing the Driver reported from the road, with what became of it. The Driver app owns nothing of the TMS's: it holds a report only
/// until the Driver reports Logic App tells it the TMS recorded it, refused it, or was never reached (P12, ADR 0016).
/// </summary>
public sealed class DriverReport : EntityBase<ReportId>, IAggregateRoot
{
    private DriverReport()
    {
        CorrelationId = null!;
    }

    public ReportKind Kind { get; private set; }

    public TripNumber TripNumber { get; private set; }

    /// <summary>The Driver ID from the Driver's token when they reported.</summary>
    public DriverId DriverId { get; private set; }

    /// <summary>The version of the Trip the Driver app held, for tracing.</summary>
    public int VersionHeld { get; private set; }

    public DateTimeOffset EventTime { get; private set; }

    /// <summary>The correlation ID of the Driver's action, which the event carries.</summary>
    public string CorrelationId { get; private set; }

    public ReportState State { get; private set; }

    /// <summary>The TMS's failure code of a refusal, or the integration layer's of a report that wasn't recorded.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>The readable message of a refusal, or the technical cause of a report that wasn't recorded.</summary>
    public string? Message { get; private set; }

    public DateTimeOffset? OutcomeAt { get; private set; }

    /// <summary>A Start Trip the Driver can't send again: the TMS has it, or hasn't answered yet.</summary>
    public bool IsActiveStart => Kind == ReportKind.StartTrip && State is ReportState.Sending or ReportState.Recorded;

    public static DriverReport StartTrip(ReportId id, TripNumber tripNumber, DriverId driverId, int versionHeld, DateTimeOffset eventTime, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId.Value);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionHeld, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new DriverReport
        {
            Id = id,
            Kind = ReportKind.StartTrip,
            TripNumber = tripNumber,
            DriverId = driverId,
            VersionHeld = versionHeld,
            EventTime = eventTime,
            CorrelationId = correlationId,
            State = ReportState.Sending,
        };
    }

    /// <summary>The TMS accepted the report. False, and nothing changes, when the report has an outcome already: the Logic App may say it again.</summary>
    public bool Record(DateTimeOffset at) => Conclude(ReportState.Recorded, null, null, at);

    /// <summary>The TMS refused the report, with its failure code and message.</summary>
    public bool Refuse(string failureCode, string message, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Conclude(ReportState.Refused, failureCode, message, at);
    }

    /// <summary>The Logic App gave up: the TMS wasn't reached after its retries, or the report couldn't be handed to it.</summary>
    public bool GiveUp(string? failureCode, string cause, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cause);
        return Conclude(ReportState.NotRecorded, failureCode, cause, at);
    }

    private bool Conclude(ReportState state, string? failureCode, string? message, DateTimeOffset at)
    {
        if (State != ReportState.Sending)
        {
            return false;
        }

        State = state;
        FailureCode = failureCode;
        Message = message;
        OutcomeAt = at;
        return true;
    }
}
