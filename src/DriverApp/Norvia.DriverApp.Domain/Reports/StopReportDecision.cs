namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver app does with a report the Driver wants to make on a Stop.</summary>
public abstract record StopReportDecision
{
    private StopReportDecision()
    {
    }

    /// <summary>The report is made and published.</summary>
    public sealed record Accept : StopReportDecision;

    /// <summary>The Driver has made this report already and the TMS has it or hasn't answered yet: nothing is published.</summary>
    public sealed record Repeat(ReportId ReportId) : StopReportDecision;

    /// <summary>The report is not well formed.</summary>
    public sealed record Invalid(IReadOnlyList<StopReportProblem> Problems) : StopReportDecision;

    /// <summary>The Stop has an outcome already, and this report would give it another.</summary>
    public sealed record AlreadyCompleted : StopReportDecision;

    /// <summary>The Stop cannot be reported on now.</summary>
    public sealed record Unavailable(StopBlock Block) : StopReportDecision;
}
