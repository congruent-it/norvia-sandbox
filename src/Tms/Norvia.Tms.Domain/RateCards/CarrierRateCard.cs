using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of Norvia's agreement with a Carrier that sets what Norvia pays it for transport. It
/// has the shape of a <see cref="RateCard"/> and is priced the same way. The ERP owns it and the TMS never edits it
/// (ADR 0013); the ERP replicates each change (E3) as the whole list of dated versions; until one arrives the copy is the seed file's, at Master-data revision 1. It keeps the <see cref="RateCardId"/> and the
/// version reference of a Rate Card for now, as the calculation is shared; the Carrier cost that replaces the Freight
/// Charge as its result gets its own types when release pricing (P9) needs them.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record CarrierRateCard(RateCardId Id, CarrierId CarrierId, IReadOnlyList<RateCardVersion> Versions, int Revision = 1)
{
    /// <summary>Prices a Transport Order from the version valid on its collection date, or says why it can't.</summary>
    public RatingOutcome Rate(TransportOrderDetails details) =>
        RateCardRating.Rate(RateCardKind.CarrierRateCard, Id, Versions, details);
}
