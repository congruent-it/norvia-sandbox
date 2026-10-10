namespace Norvia.Tms.Domain.Trips;

/// <summary>What became of a Trip dispatch Logic App's delivery report (P11).</summary>
public enum TripDeliveryReport
{
    /// <summary>The report set the version's delivery state.</summary>
    Recorded = 1,

    /// <summary>The version is no longer Sending, so this is a repeat or a late report: nothing changed.</summary>
    Ignored,

    /// <summary>The Trip never had that version.</summary>
    UnknownVersion,
}
