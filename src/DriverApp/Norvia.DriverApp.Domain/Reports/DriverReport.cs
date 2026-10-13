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

    /// <summary>The Shipment a Stop report is about; null for a Start Trip or an End Trip.</summary>
    public string? ShipmentNumber { get; private set; }

    /// <summary>The Stop of that Shipment a Stop report is about; null for a Start Trip or an End Trip.</summary>
    public StopKind? Stop { get; private set; }

    /// <summary>What the Driver said about a Stop report: required for a failure or damage.</summary>
    public string? Remark { get; private set; }

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

    /// <summary>Whether the report completes a Stop (Collected, Collection failed, Delivered, Delivery failed). Report damage and a Proof of delivery don't.</summary>
    public bool CompletesStop => CompletesAStop(Kind);

    public static bool CompletesAStop(ReportKind kind) => kind is ReportKind.Collected or ReportKind.CollectionFailed or ReportKind.Delivered or ReportKind.DeliveryFailed;

    public static DriverReport StartTrip(ReportId id, TripNumber tripNumber, DriverId driverId, int versionHeld, DateTimeOffset eventTime, string correlationId) =>
        OfTrip(id, ReportKind.StartTrip, tripNumber, driverId, versionHeld, eventTime, correlationId);

    /// <summary>The Driver ends the Trip once every Stop has an outcome. The TMS decides all the same.</summary>
    public static DriverReport EndTrip(ReportId id, TripNumber tripNumber, DriverId driverId, int versionHeld, DateTimeOffset eventTime, string correlationId) =>
        OfTrip(id, ReportKind.EndTrip, tripNumber, driverId, versionHeld, eventTime, correlationId);

    private static DriverReport OfTrip(ReportId id, ReportKind kind, TripNumber tripNumber, DriverId driverId, int versionHeld, DateTimeOffset eventTime, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId.Value);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionHeld, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new DriverReport
        {
            Id = id,
            Kind = kind,
            TripNumber = tripNumber,
            DriverId = driverId,
            VersionHeld = versionHeld,
            EventTime = eventTime,
            CorrelationId = correlationId,
            State = ReportState.Sending,
        };
    }

    /// <summary>The Driver completes a Stop with a Collected, Collection failed or Delivery failed, or reports damage at a Stop of any kind.</summary>
    public static DriverReport ReportStop(ReportId id, ReportKind kind, StopRef stop, ReportOrigin origin, string? remark)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (kind is ReportKind.StartTrip or ReportKind.EndTrip or ReportKind.Delivered or ReportKind.ProofOfDelivery)
        {
            throw new ArgumentException($"{kind} is not a report on a Stop made on its own.", nameof(kind));
        }

        if (!StopReportRules.Fits(kind, stop.Kind))
        {
            throw new ArgumentException($"{kind} is not reported on a {stop.Kind} Stop.", nameof(stop));
        }

        if (StopReportRules.NeedsRemark(kind))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(remark);
        }

        return Of(id, kind, stop, origin, remark);
    }

    /// <summary>
    /// The Driver delivers a Stop with a Proof of delivery: two reports to two TMS operations (P5 and P6 keep the Tracking event and the Proof of delivery
    /// apart) that share the Driver's action, so one correlation ID. The remark is the Driver's reservations, and only with reservations.
    /// </summary>
    public static DeliveredReports Deliver(ReportId deliveredId, ReportId proofOfDeliveryId, StopRef stop, ReportOrigin origin, string? reservations)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (stop.Kind != StopKind.Delivery)
        {
            throw new ArgumentException("Only a delivery Stop is delivered.", nameof(stop));
        }

        return new(Of(deliveredId, ReportKind.Delivered, stop, origin, reservations), Of(proofOfDeliveryId, ReportKind.ProofOfDelivery, stop, origin, reservations));
    }

    private static DriverReport Of(ReportId id, ReportKind kind, StopRef stop, ReportOrigin origin, string? remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stop.ShipmentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin.DriverId.Value);
        ArgumentOutOfRangeException.ThrowIfLessThan(origin.VersionHeld, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin.CorrelationId);
        return new DriverReport
        {
            Id = id,
            Kind = kind,
            TripNumber = origin.TripNumber,
            ShipmentNumber = stop.ShipmentNumber,
            Stop = stop.Kind,
            DriverId = origin.DriverId,
            VersionHeld = origin.VersionHeld,
            EventTime = origin.EventTime,
            CorrelationId = origin.CorrelationId,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark,
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
