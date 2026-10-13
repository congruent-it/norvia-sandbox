namespace Norvia.DriverApp.Domain.Reports;

/// <summary>Why a Stop cannot be completed now.</summary>
public enum StopBlock
{
    None,
    TripNotStarted,
    CollectFirst,
    CollectionFailed,
}
