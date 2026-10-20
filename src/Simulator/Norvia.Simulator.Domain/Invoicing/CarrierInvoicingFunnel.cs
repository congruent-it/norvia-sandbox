namespace Norvia.Simulator.Domain.Invoicing;

/// <summary>
/// Makes the Carriers' invoicing journeys (E2): exactly the share asked for of the invoices carry one edited amount, and of the lines left To decide Finance rejects
/// exactly the share asked for, at positions the seed picks, so the same seed and the same numbers give the same journeys.
/// </summary>
public static class CarrierInvoicingFunnel
{
    // Apart from the bookings', the Planner's, the Carriers', the Drivers' and the Shippers' invoicing own random sequences.
    private const int EditSalt = 0x43494E45;
    private const int DecisionSalt = 0x43494E44;

    /// <summary>
    /// For each invoice, in order, the position of the line that carries an edited amount, or null for an invoice sent as the Carrier agreed it. An invoice
    /// with <paramref name="lineCounts"/>[i] lines edits one of them.
    /// </summary>
    public static IReadOnlyList<int?> EditedLines(InvoicingShares shares, int seed, IReadOnlyList<int> lineCounts)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(lineCounts);

        var random = new SeededRandom(unchecked(seed ^ EditSalt));
        var edited = Pick(random, lineCounts.Count, shares.EditedCarrierInvoicePercent).ToHashSet();
        return [.. lineCounts.Select((count, invoice) => edited.Contains(invoice) && count > 0 ? random.Next(count) : (int?)null)];
    }

    /// <summary>The positions, among the lines left To decide, in order, that Finance rejects: exactly the share asked for.</summary>
    public static IReadOnlySet<int> RejectedLines(InvoicingShares shares, int seed, int toDecideCount)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentOutOfRangeException.ThrowIfNegative(toDecideCount);

        return Pick(new SeededRandom(unchecked(seed ^ DecisionSalt)), toDecideCount, shares.RejectedCarrierLinePercent).ToHashSet();
    }

    // Exactly the percentage of count, picked by the seed.
    private static IEnumerable<int> Pick(SeededRandom random, int count, int percent)
    {
        var wanted = (int)Math.Round(count * percent / 100.0, MidpointRounding.AwayFromZero);
        return random.Shuffled(count).Take(Math.Min(wanted, count)).Order();
    }
}
