using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum DriverReportsRunOutcome
{
    /// <summary>The TMS recorded or refused the report, or couldn't be reached, and the Driver app was told which.</summary>
    Done,

    /// <summary>The message isn't a <c>TripStartReported</c>, a <c>TripEndReported</c>, a <c>StopCompletedReported</c> or a <c>ProofOfDeliveryCaptured</c> the TMS can be asked to record, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The Driver app didn't take the outcome, so the report stays Sending there. The TMS has it anyway.</summary>
    OutcomeNotReported,

    /// <summary>The files of a Proof of delivery are gone from the transit container, so nothing was sent to the TMS: the message was dead-lettered and the report is Not recorded.</summary>
    FilesGone,
}

/// <summary>
/// The P12 Driver reports Logic App's steps, played locally (ADR 0016): for a Proof of delivery fetch its files from the Driver app's transit container (ADR 0009),
/// call the TMS for the Driver's report under the bounded exponential retry policy,
/// settle the message (completed when the TMS recorded it, dead-lettered with the failure code and message when it refused or couldn't be reached), then
/// tell the Driver app whether the report was Recorded, Refused or Not recorded, and delete the files of a Proof of delivery the TMS accepted. A report that fails for good is dead-lettered so the Trip's session can
/// move on to the next report.
/// </summary>
internal sealed partial class DriverReportsFlow(
    TmsTripCalls tms,
    IDriverProofFiles proofFiles,
    DriverAppOutcomes driverApp,
    IOptions<DriverReportsWorkerOptions> options,
    TimeProvider time,
    ILogger<DriverReportsFlow> logger)
{
    internal const string UnreadableDescription = "The message isn't a Trip start, a Trip end, a Stop report or a Proof of delivery the Driver app made, so nothing was sent to the TMS.";

    /// <summary>The longest message the Driver app takes in an outcome report.</summary>
    private const int MaximumMessageLength = 1000;

    public async Task<DriverReportsRunOutcome> RunAsync(string message, Func<Settlement, CancellationToken, Task> settle, CancellationToken cancellationToken, string? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(settle);

        if (DriverReport.Read(message) is not { } report)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, DriverReportsFailureCodes.Unreadable, messageId));
            LogUnreadable();
            await settle(new Settlement.DeadLetter(DriverReportsFailureCodes.Unreadable, UnreadableDescription), cancellationToken);
            return DriverReportsRunOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(report.ContractKeys);
        IReadOnlyList<FetchedProofFile> files = [];
        if (report.Proof is { } proof)
        {
            if (await proofFiles.FetchAsync(proof.Files, cancellationToken) is not { } fetched)
            {
                return await GiveUpOnGoneFilesAsync(report, settle, messageId, cancellationToken);
            }

            files = fetched;
        }

        var (answer, attempts) = await WithRetriesAsync(
            () => tms.CallAsync(report, files, cancellationToken), result => result.StatusCode, DriverReportsFailureCodes.TmsUnavailable, cancellationToken);

        var (settlement, outcome) = Decide(report, answer, attempts);
        using (logger.BeginScope(TrackingKeys.OutcomeOf(
            settlement is Settlement.DeadLetter deadLettered ? TrackingKeys.DeadLettered : TrackingKeys.Succeeded, (settlement as Settlement.DeadLetter)?.Reason, messageId)))
        {
            LogSettled(outcome.Outcome, answer.StatusCode);
            await settle(settlement, cancellationToken);
        }

        // The message is settled first, as the workflow does: a run that fails to report the outcome isn't redelivered, and the report stays Sending.
        var (status, _) = await WithRetriesAsync(
            () => driverApp.ReportAsync(report, outcome, cancellationToken), code => code, DriverReportsFailureCodes.DriverAppUnavailable, cancellationToken);
        var reported = HttpStatus.IsSuccess(status);
        var failureCode = reported ? null : HttpStatus.IsTransient(status) ? DriverReportsFailureCodes.DriverAppUnavailable : DriverReportsFailureCodes.DriverAppRefused;
        using (logger.BeginScope(TrackingKeys.OutcomeOf(reported ? TrackingKeys.Succeeded : TrackingKeys.Failed, failureCode)))
        {
            LogOutcomeReported(outcome.Outcome, status);
        }

        // Only a Proof of delivery the TMS accepted is deleted; a refused one stays for investigation, until the container's lifecycle rule deletes it.
        if (report.Proof is { } accepted && answer.Succeeded)
        {
            await proofFiles.DeleteAsync(accepted.Files, cancellationToken);
        }

        return reported ? DriverReportsRunOutcome.Done : DriverReportsRunOutcome.OutcomeNotReported;
    }

    /// <summary>The files are gone: the message is dead-lettered and the Driver app is told the report wasn't recorded, and the run fails, as the workflow's does.</summary>
    private async Task<DriverReportsRunOutcome> GiveUpOnGoneFilesAsync(DriverReport report, Func<Settlement, CancellationToken, Task> settle, string? messageId, CancellationToken cancellationToken)
    {
        const string description = "The Proof of delivery's files could not be read from the Driver app's transit container, so nothing was sent to the TMS.";
        using (logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, DriverReportsFailureCodes.ProofOfDeliveryFilesGone, messageId)))
        {
            LogFilesGone();
            await settle(new Settlement.DeadLetter(DriverReportsFailureCodes.ProofOfDeliveryFilesGone, description), cancellationToken);
        }

        // Whether the Driver app took the outcome doesn't change what the run is: it fails either way.
        _ = await WithRetriesAsync(
            () => driverApp.ReportAsync(report, OutcomeReport.For(report, ReportOutcome.NotRecorded, DriverReportsFailureCodes.ProofOfDeliveryFilesGone, description), cancellationToken),
            code => code, DriverReportsFailureCodes.DriverAppUnavailable, cancellationToken);
        return DriverReportsRunOutcome.FilesGone;
    }

    private static (Settlement Settlement, OutcomeReport Outcome) Decide(DriverReport report, TmsAnswer answer, int attempts)
    {
        if (answer.Succeeded)
        {
            return (Settlement.Complete, OutcomeReport.For(report, ReportOutcome.Recorded));
        }

        if (answer.IsTransient)
        {
            var cause = Shorten($"The TMS {(answer.StatusCode == 0 ? "didn't answer" : $"answered {answer.StatusCode}")}: {answer.Message} ({attempts} {(attempts == 1 ? "attempt" : "attempts")}).");
            return (new Settlement.DeadLetter(DriverReportsFailureCodes.TmsUnavailable, cause), OutcomeReport.For(report, ReportOutcome.NotRecorded, DriverReportsFailureCodes.TmsUnavailable, cause));
        }

        var code = answer.Code ?? DriverReportsFailureCodes.TmsCallRefused;
        var refusal = Shorten(answer.Message);
        return (new Settlement.DeadLetter(code, refusal), OutcomeReport.For(report, ReportOutcome.Refused, code, refusal));
    }

    private static string Shorten(string message) => message.Length <= MaximumMessageLength ? message : message[..MaximumMessageLength];

    /// <summary>Calls until the call isn't worth trying again, or the retries are used up, waiting longer each time up to the maximum.</summary>
    private async Task<(T Result, int Attempts)> WithRetriesAsync<T>(Func<Task<T>> call, Func<T, int> statusOf, string failureCode, CancellationToken cancellationToken)
    {
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var result = await call();
            var status = statusOf(result);
            if (!HttpStatus.IsTransient(status) || attempt >= retry.Count)
            {
                return (result, attempt + 1);
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, failureCode));
            LogRetrying(status, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't a Trip start, a Trip end, a Stop report or a Proof of delivery the Driver app made")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The files of the Proof of delivery are gone from the transit container")]
    private partial void LogFilesGone();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The call answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "The report is {Outcome} (the TMS answered {StatusCode}); settling the message")]
    private partial void LogSettled(ReportOutcome outcome, int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Told the Driver app the report is {Outcome}; it answered {StatusCode}")]
    private partial void LogOutcomeReported(ReportOutcome outcome, int statusCode);
}
