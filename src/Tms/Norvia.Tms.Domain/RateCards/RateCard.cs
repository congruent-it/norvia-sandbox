using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// The TMS's read copy of the part of a Shipper's Contract that sets what Norvia charges for transport, by Lane and
/// weight. The ERP owns it and the TMS never edits it (ADR 0006); until the ERP sync exists the copy comes from the
/// seed file.
/// </summary>
public sealed record RateCard(string Id, ShipperId ShipperId, IReadOnlyList<RateCardVersion> Versions)
{
    /// <summary>
    /// The Freight Charge for a Transport Order, from the version valid on its collection date, or null when this Rate
    /// Card can't price it.
    /// </summary>
    public FreightCharge? FreightChargeFor(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (CollectionDate.Of(details) is not { } collectionDate
            || Versions.FirstOrDefault(version => version.IsValidOn(collectionDate)) is not { } version
            || version.FreightFor(details) is not { } freight)
        {
            return null;
        }

        return new FreightCharge([new ChargeLine(ChargeLineType.Freight, freight)], new RateCardVersionReference(Id, version.Version));
    }
}
