namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>
/// Ranks the Open Carrier costs of a Carrier as likely candidates for an Unknown booking line (the Carrier mangled its reference, or
/// the Carrier cost has not arrived under it). The ERP ranks, never the model: on how alike the references are (60%), how close the
/// amount is to the Carrier cost's total (30%) and how soon after delivery the invoice is dated (10%). A candidate is a likely one only:
/// nothing here proves the line is about it.
/// </summary>
public static class CandidateCarrierCosts
{
    /// <summary>The most candidates the ERP offers.</summary>
    public const int MaxCandidates = 3;

    /// <summary>A reference at least this alike is a candidate whatever the amount.</summary>
    public const double MinimumReferenceSimilarity = 0.5;

    /// <summary>An amount at least this close to the total is a candidate whatever the reference.</summary>
    public const double MinimumAmountCloseness = 0.99;

    private const double ReferenceWeight = 0.6;
    private const double AmountWeight = 0.3;
    private const double DeliveryWeight = 0.1;

    /// <param name="openCosts">The Open Carrier costs of the line's Carrier. Costs that are not Open are left out.</param>
    public static IReadOnlyList<CandidateCarrierCost> Rank(string carrierBookingReference, decimal amount, DateOnly invoiceDate, IEnumerable<CarrierCost> openCosts)
    {
        ArgumentNullException.ThrowIfNull(carrierBookingReference);
        ArgumentNullException.ThrowIfNull(openCosts);

        return
        [
            .. openCosts
                .Where(cost => cost.Status == CarrierCostStatus.Open)
                .Select(cost => (Cost: cost, Reference: ReferenceSimilarity(carrierBookingReference, cost.CarrierBookingReference), Amount: AmountCloseness(amount, cost.Total)))
                .Where(scored => scored.Reference >= MinimumReferenceSimilarity || scored.Amount >= MinimumAmountCloseness)
                .Select(scored => new CandidateCarrierCost(
                    scored.Cost,
                    Math.Round((ReferenceWeight * scored.Reference) + (AmountWeight * scored.Amount) + (DeliveryWeight * DeliveryProximity(invoiceDate, scored.Cost)), 4)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Cost.Id.CarrierBookingNumber, StringComparer.Ordinal)
                .Take(MaxCandidates),
        ];
    }

    /// <summary>1 for references that are the same once case, spaces and punctuation are ignored, falling to 0 as they differ.</summary>
    public static double ReferenceSimilarity(string left, string right)
    {
        var a = Normalise(left);
        var b = Normalise(right);
        var longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 0 : 1d - ((double)EditDistance(a, b) / longest);
    }

    /// <summary>1 for equal amounts, falling to 0 as the amounts grow apart.</summary>
    public static double AmountCloseness(decimal invoiced, decimal agreed)
    {
        var larger = Math.Max(Math.Abs(invoiced), Math.Abs(agreed));
        return larger == 0 ? 1 : Math.Max(0d, 1d - (double)(Math.Abs(invoiced - agreed) / larger));
    }

    // A Carrier cost delivered on or before the invoice date is the likelier one, the nearer the better; one delivered after it cannot be what the invoice is for.
    private static double DeliveryProximity(DateOnly invoiceDate, CarrierCost cost)
    {
        var days = invoiceDate.DayNumber - DateOnly.FromDateTime(cost.DeliveryTime.Date).DayNumber;
        return days < 0 ? 0 : 1d / (1d + (days / 30d));
    }

    private static string Normalise(string reference) => new([.. reference.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
