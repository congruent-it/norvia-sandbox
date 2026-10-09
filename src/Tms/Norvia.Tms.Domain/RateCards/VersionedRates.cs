using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The calculation a Rate Card and a Carrier Rate Card share (P2, P9): choose the version valid on the collection date,
/// then match the Lane and the Weight Break. Only the name of the card in a failure message differs.
/// </summary>
internal static class VersionedRates
{
    internal const string RateCardName = "Rate Card";
    internal const string CarrierRateCardName = "Carrier Rate Card";

    public static RatingOutcome Rate(string cardName, RateCardId id, IReadOnlyList<RateCardVersion> versions, TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // An Accepted Transport Order always has a collection window; without one no version can be chosen.
        if (CollectionDate.Of(details) is not { } collectionDate)
        {
            return RatingOutcome.Failed(RatingFailureCode.NoValidRateCardVersion, Invariant($"The Transport Order has no collection date to choose a version of {cardName} {id} by."));
        }

        return versions.FirstOrDefault(version => version.IsValidOn(collectionDate)) is { } version
            ? version.Rate(cardName, id, details)
            : RatingOutcome.Failed(RatingFailureCode.NoValidRateCardVersion, Invariant($"No version of {cardName} {id} is valid on the collection date, {collectionDate:yyyy-MM-dd}."));
    }
}
