using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Reports.ReportOutcome;

/// <summary>
/// Gives a report the outcome the Logic App reports. The Logic App may say an outcome twice, and a late one must not undo an earlier one,
/// so a report that has an outcome already is left as it is.
/// </summary>
public sealed class ReportOutcomeHandler(IRepository<DriverReport> reports, TimeProvider time)
    : ICommandHandler<ReportOutcomeCommand, Result<ReportOutcomeResult>>
{
    /// <summary>The longest message the Driver is shown: the Logic App sends the TMS's, or a technical cause.</summary>
    internal const int MaximumMessageLength = 1000;

    public async ValueTask<Result<ReportOutcomeResult>> Handle(ReportOutcomeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (Validate(command) is { Count: > 0 } errors)
        {
            return Result.Invalid(errors);
        }

        if (await reports.GetByIdAsync(new ReportId(command.DriverAppEventId), cancellationToken) is not { } report
            || report.TripNumber != new TripNumber(command.TripNumber))
        {
            return Result.NotFound();
        }

        var now = time.GetUtcNow();
        var message = command.Message is { } text && text.Length > MaximumMessageLength ? text[..MaximumMessageLength] : command.Message;
        var applied = command.Outcome switch
        {
            ReportOutcomeKind.Recorded => report.Record(now),
            ReportOutcomeKind.Refused => report.Refuse(command.FailureCode!, message!, now),
            _ => report.GiveUp(command.FailureCode, message!, now),
        };
        if (!applied)
        {
            return ReportOutcomeResult.Ignored;
        }

        await reports.UpdateAsync(report, cancellationToken);
        return ReportOutcomeResult.Applied;
    }

    private static List<ValidationError> Validate(ReportOutcomeCommand command)
    {
        var errors = new List<ValidationError>();
        if (command.Outcome == ReportOutcomeKind.Refused && string.IsNullOrWhiteSpace(command.FailureCode))
        {
            errors.Add(new("failureCode", "A refusal needs the TMS's failure code."));
        }

        if (command.Outcome != ReportOutcomeKind.Recorded && string.IsNullOrWhiteSpace(command.Message))
        {
            errors.Add(new("message", "A refusal or a report that wasn't recorded needs its message."));
        }

        return errors;
    }
}
