namespace Norvia.Simulator.Domain.Drivers;

/// <summary>
/// What goes wrong on the road, one percentage each, a share of the Shipments on today's Trips: collections that fail (the Shipment goes back to
/// Planned), deliveries that fail (the Shipment waits for a Re-delivery), Shipments the Driver reports damage on, and deliveries with reservations. Each
/// failure opens an Exception at the TMS (P7). A Shipment fails at most once, and one that is delivered can have reservations.
/// </summary>
public sealed record DriverShares(
    int CollectionFailedPercent = DriverShares.DefaultCollectionFailedPercent,
    int DeliveryFailedPercent = DriverShares.DefaultDeliveryFailedPercent,
    int DamagedPercent = DriverShares.DefaultDamagedPercent,
    int ReservationsPercent = DriverShares.DefaultReservationsPercent)
{
    public const int DefaultCollectionFailedPercent = 10;
    public const int DefaultDeliveryFailedPercent = 10;
    public const int DefaultDamagedPercent = 5;
    public const int DefaultReservationsPercent = 10;

    /// <summary>Everything wrong with these shares; empty when they may be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Check(problems, CollectionFailedPercent, "The share of Shipments whose collection fails");
        Check(problems, DeliveryFailedPercent, "The share of Shipments whose delivery fails");
        Check(problems, DamagedPercent, "The share of Shipments the Driver reports damage on");
        Check(problems, ReservationsPercent, "The share of delivered Shipments with reservations");

        if (CollectionFailedPercent is >= 0 and <= 100 && DeliveryFailedPercent is >= 0 and <= 100 && CollectionFailedPercent + DeliveryFailedPercent > 100)
        {
            problems.Add("The collections and the deliveries that fail can't be more than all of the Shipments between them.");
        }

        return problems;
    }

    private static void Check(List<string> problems, int percent, string what)
    {
        if (percent is < 0 or > 100)
        {
            problems.Add($"{what} is a percentage from 0 to 100.");
        }
    }
}
