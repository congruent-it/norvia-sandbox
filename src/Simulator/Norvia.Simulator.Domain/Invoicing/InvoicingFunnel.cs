namespace Norvia.Simulator.Domain.Invoicing;

/// <summary>
/// Makes Finance's journeys for the run's Shippers: exactly the share asked for at each step, at positions the seed picks, so the same seed and the same number
/// of Shippers give the same journeys. A Credit note is only ever on an invoice.
/// </summary>
public static class InvoicingFunnel
{
    // Apart from the bookings', the Planner's, the Carriers' and the Drivers' own random sequences.
    private const int InvoicingSalt = 0x494E5643;

    public static IReadOnlyList<InvoicingJourney> Plan(InvoicingShares shares, int seed, int shipperCount)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentOutOfRangeException.ThrowIfNegative(shipperCount);

        var random = new SeededRandom(unchecked(seed ^ InvoicingSalt));
        var invoiced = Pick(random, shipperCount, shares.InvoicePercent).ToHashSet();
        var invoicedInOrder = Enumerable.Range(0, shipperCount).Where(invoiced.Contains).ToList();
        var credited = Pick(random, invoicedInOrder.Count, shares.CreditNotePercent).Select(position => invoicedInOrder[position]).ToHashSet();
        return [.. Enumerable.Range(0, shipperCount).Select(index => new InvoicingJourney(index, invoiced.Contains(index), credited.Contains(index)))];
    }

    public static InvoicingFunnelCounts Count(IReadOnlyList<InvoicingJourney> journeys)
    {
        ArgumentNullException.ThrowIfNull(journeys);
        return new InvoicingFunnelCounts(journeys.Count, journeys.Count(journey => journey.Invoice), journeys.Count(journey => journey.CreditNote));
    }

    // Exactly the percentage of count, picked by the seed.
    private static IEnumerable<int> Pick(SeededRandom random, int count, int percent)
    {
        var wanted = (int)Math.Round(count * percent / 100.0, MidpointRounding.AwayFromZero);
        return random.Shuffled(count).Take(Math.Min(wanted, count)).Order();
    }
}

public sealed record InvoicingFunnelCounts(int Shippers, int Invoiced, int Credited);
