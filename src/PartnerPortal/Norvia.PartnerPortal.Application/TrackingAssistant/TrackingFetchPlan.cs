using System.Text.RegularExpressions;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;


/// <summary>The portal chooses the data, never the model: the open order and the list always, and this one extra call for a named order.</summary>
public static partial class TrackingFetchPlan
{
    public static ExtraFetch? Extra(string question, ShipperTrackingDetail? open, IReadOnlyList<ShipperTrackingSummary> orders)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(orders);

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in orders.SelectMany(order => new[] { order.TransportOrderNumber, order.ShipperReference }).Append(open?.TransportOrderNumber).Append(open?.ShipperReference))
        {
            if (value is not null)
            {
                known.Add(value);
            }
        }

        var withoutTimes = TrackingPatterns.Time().Replace(TrackingPatterns.Date().Replace(question, " "), " ");
        foreach (Match match in Candidate().Matches(withoutTimes))
        {
            if (!match.Value.Any(char.IsDigit) || known.Contains(match.Value))
            {
                continue;
            }

            return TransportOrderNumber().IsMatch(match.Value)
                ? new ExtraFetch(ExtraFetchKind.GetNumber, match.Value.ToUpperInvariant())
                : new ExtraFetch(ExtraFetchKind.FindReference, match.Value);
        }

        return null;
    }

    [GeneratedRegex(@"\b[A-Za-z0-9]+(?:[-/][A-Za-z0-9]+)+\b")]
    private static partial Regex Candidate();

    [GeneratedRegex(@"^TO-\d{7}$", RegexOptions.IgnoreCase)]
    private static partial Regex TransportOrderNumber();
}
