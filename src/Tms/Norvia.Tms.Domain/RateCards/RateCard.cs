using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of a Shipper's Contract that sets what Norvia charges for transport, by Lane and
/// weight. The ERP owns it and the TMS never edits it (ADR 0006); the ERP replicates each change (E3) as the whole list of dated
/// versions; until one arrives the copy is the seed file's, at Master-data revision 1.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record RateCard(RateCardId Id, ShipperId ShipperId, IReadOnlyList<RateCardVersion> Versions, int Revision = 1)
{
    /// <summary>
    /// Rates a Transport Order from the version valid on its collection date: its Freight Charge, or why this Rate Card
    /// can't price it.
    /// </summary>
    public RatingOutcome Rate(TransportOrderDetails details) =>
        RateCardRating.Rate(RateCardKind.RateCard, Id, Versions, details);
}
