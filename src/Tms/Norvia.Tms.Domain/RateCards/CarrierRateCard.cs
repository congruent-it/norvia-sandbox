using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of Norvia's agreement with a Carrier that sets what Norvia pays it for transport. It
/// has the shape of a <see cref="RateCard"/> and is priced the same way. The ERP owns it and the TMS never edits it
/// (ADR 0013); until the ERP sync exists the copy comes from the seed file.
/// </summary>
public sealed record CarrierRateCard(RateCardId Id, CarrierId CarrierId, IReadOnlyList<RateCardVersion> Versions)
{
    /// <summary>Prices a Transport Order from the version valid on its collection date, or says why it can't.</summary>
    public RatingOutcome Rate(TransportOrderDetails details) =>
        VersionedRates.Rate(VersionedRates.CarrierRateCardName, Id, Versions, details);
}
