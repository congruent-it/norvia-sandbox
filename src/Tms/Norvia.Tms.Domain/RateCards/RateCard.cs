using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of a Shipper's Contract that sets what Norvia charges for transport, by Lane and
/// weight. The ERP owns it and the TMS never edits it (ADR 0006); until the ERP sync exists the copy comes from the
/// seed file.
/// </summary>
public sealed record RateCard(RateCardId Id, ShipperId ShipperId, IReadOnlyList<RateCardVersion> Versions)
{
    /// <summary>
    /// Rates a Transport Order from the version valid on its collection date: its Freight Charge, or why this Rate Card
    /// can't price it.
    /// </summary>
    public RatingOutcome Rate(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // An Accepted Transport Order always has a collection window; without one no version can be chosen.
        if (CollectionDate.Of(details) is not { } collectionDate)
        {
            return RatingOutcome.Failed(RatingFailureCode.NoValidRateCardVersion, Invariant($"The Transport Order has no collection date to choose a version of Rate Card {Id} by."));
        }

        return Versions.FirstOrDefault(version => version.IsValidOn(collectionDate)) is { } version
            ? version.Rate(Id, details)
            : RatingOutcome.Failed(RatingFailureCode.NoValidRateCardVersion, Invariant($"No version of Rate Card {Id} is valid on the collection date, {collectionDate:yyyy-MM-dd}."));
    }
}
