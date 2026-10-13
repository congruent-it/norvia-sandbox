using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// What the Driver app lets a Driver report on a Stop (P12): the report fits the kind of Stop, a failure or damage has a remark, a Stop is completed once, in
/// any order, but a delivery waits for its collection, and the Stop has the address the TMS needs as the place. The TMS decides all the same; these are the
/// rules the Driver app can know.
/// </summary>
public static class StopReportRules
{
    /// <summary>The longest remark the TMS keeps of a Tracking event.</summary>
    public const int MaximumRemarkLength = 500;

    /// <summary>Collected and Collection failed are reported on a collection Stop, Delivered (with its Proof of delivery) and Delivery failed on a delivery Stop, and damage on either.</summary>
    public static bool Fits(ReportKind kind, StopKind stop) => kind switch
    {
        ReportKind.Collected or ReportKind.CollectionFailed => stop == StopKind.Collection,
        ReportKind.Delivered or ReportKind.ProofOfDelivery or ReportKind.DeliveryFailed => stop == StopKind.Delivery,
        ReportKind.Damaged => true,
        _ => false,
    };

    /// <summary>
    /// Everything but a plain Collected needs the Driver's words, and a Delivered says them only with reservations, which the Proof of delivery's own rules
    /// ask for (<see cref="ProofOfDeliveryRules"/>).
    /// </summary>
    public static bool NeedsRemark(ReportKind kind) => kind is not (ReportKind.Collected or ReportKind.StartTrip or ReportKind.Delivered or ReportKind.ProofOfDelivery);

    public static StopReportDecision Decide(StopStatus status, ReportKind kind, string? remark)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (Check(status, kind, remark) is { Count: > 0 } problems)
        {
            return new StopReportDecision.Invalid(problems);
        }

        if (kind == ReportKind.Damaged)
        {
            if (!status.CanReportDamage)
            {
                return new StopReportDecision.Unavailable(StopBlock.TripNotStarted);
            }

            // Pressing Report damage twice with the same words while the first is on its way is one report.
            return status.Reports.FirstOrDefault(report => report.Kind == ReportKind.Damaged && report.State == ReportState.Sending && report.Remark == remark) is { } sending
                ? new StopReportDecision.Repeat(sending.Id)
                : new StopReportDecision.Accept();
        }

        if (status.Completed)
        {
            return status.Outcome!.Kind == kind ? new StopReportDecision.Repeat(status.Outcome.Id) : new StopReportDecision.AlreadyCompleted();
        }

        return status.Block == StopBlock.None ? new StopReportDecision.Accept() : new StopReportDecision.Unavailable(status.Block);
    }

    private static List<StopReportProblem> Check(StopStatus status, ReportKind kind, string? remark)
    {
        var problems = new List<StopReportProblem>();
        if (!Fits(kind, status.Stop.Kind))
        {
            problems.Add(new("action", $"{kind} is not reported on a {status.Stop.Kind.ToString().ToLowerInvariant()} Stop."));
        }

        if (NeedsRemark(kind) && string.IsNullOrWhiteSpace(remark))
        {
            problems.Add(new("remark", "A remark is required."));
        }

        if (remark?.Length > MaximumRemarkLength)
        {
            problems.Add(new("remark", $"A remark is at most {MaximumRemarkLength} characters."));
        }

        // The TMS records where a Stop report happened, and the Stop's address is that place.
        if (status.Details?.Address is not { City: { Length: > 0 }, Postcode: { Length: > 0 }, Country: { Length: > 0 } })
        {
            problems.Add(new("address", "The Stop has no complete address to report from."));
        }

        return problems;
    }
}
