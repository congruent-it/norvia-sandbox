using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>What the Driver is told when a Stop cannot be completed now.</summary>
internal static class StopBlocks
{
    public static string? Text(StopBlock block) => block switch
    {
        StopBlock.None => null,
        StopBlock.TripNotStarted => "Start the Trip first.",
        StopBlock.CollectFirst => "Collect the Shipment first.",
        StopBlock.CollectionFailed => "The collection failed, so there is nothing to deliver.",
        _ => throw new ArgumentOutOfRangeException(nameof(block), block, "A block with no words."),
    };
}
