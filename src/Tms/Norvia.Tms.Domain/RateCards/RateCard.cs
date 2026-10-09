using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

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
    public RatingOutcome Rate(TransportOrderDetails details) =>
        VersionedRates.Rate(VersionedRates.RateCardName, Id, Versions, details);
}
