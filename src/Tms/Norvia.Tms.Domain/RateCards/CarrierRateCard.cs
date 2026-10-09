using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of Norvia's agreement with a Carrier that sets what Norvia pays it for transport. It
/// has the shape of a <see cref="RateCard"/> and is priced the same way. The ERP owns it and the TMS never edits it
/// (ADR 0013); until the ERP sync exists the copy comes from the seed file. It keeps the <see cref="RateCardId"/> and the
/// version reference of a Rate Card for now, as the calculation is shared; the Carrier cost that replaces the Freight
/// Charge as its result gets its own types when release pricing (P9) needs them.
/// </summary>
public sealed record CarrierRateCard(RateCardId Id, CarrierId CarrierId, IReadOnlyList<RateCardVersion> Versions)
{
    /// <summary>Prices a Transport Order from the version valid on its collection date, or says why it can't.</summary>
    public RatingOutcome Rate(TransportOrderDetails details) =>
        RateCardRating.Rate(RateCardKind.CarrierRateCard, Id, Versions, details);
}
