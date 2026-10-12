namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver sees of a report (P12, ADR 0016).</summary>
public enum ReportState
{
    /// <summary>Published, not yet reported by the Logic App.</summary>
    Sending,

    /// <summary>The TMS accepted it.</summary>
    Recorded,

    /// <summary>The TMS refused it, with its failure code and message.</summary>
    Refused,

    /// <summary>The Logic App gave up after its retries.</summary>
    NotRecorded,
}
