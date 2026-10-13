namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver app does with the Driver's End Trip.</summary>
public abstract record TripEndDecision
{
    private TripEndDecision()
    {
    }

    /// <summary>The End Trip is made and published.</summary>
    public sealed record Accept : TripEndDecision;

    /// <summary>The Driver has ended the Trip already and the TMS has it or hasn't answered yet: nothing is published.</summary>
    public sealed record Repeat(ReportId ReportId) : TripEndDecision;

    /// <summary>The TMS hasn't recorded that the Trip started.</summary>
    public sealed record NotStarted : TripEndDecision;

    /// <summary>A Stop has no outcome yet.</summary>
    public sealed record StopsOpen : TripEndDecision;
}
