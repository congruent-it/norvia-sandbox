namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>The Trip version a run sends and reports, under the correlation ID of the Dispatch or send-again it continues.</summary>
internal sealed record TripVersionRef(string TripNumber, int Version, string CorrelationId);

/// <param name="StatusCode">The TMS's HTTP status, or 0 when it didn't answer.</param>
internal sealed record TmsAnswer(int StatusCode)
{
    /// <summary>The failure code the workflow tracks for the report, or none when it was taken.</summary>
    public string? FailureCode => HttpStatus.IsSuccess(StatusCode) ? null : HttpStatus.IsTransient(StatusCode) ? TripDispatchFailureCodes.TmsUnavailable : TripDispatchFailureCodes.TmsCallRefused;

    public TripDispatchFlowOutcome Outcome =>
        HttpStatus.IsSuccess(StatusCode) ? TripDispatchFlowOutcome.Done
        : HttpStatus.IsTransient(StatusCode) ? TripDispatchFlowOutcome.TmsUnavailable
        : TripDispatchFlowOutcome.TmsRefused;
}
