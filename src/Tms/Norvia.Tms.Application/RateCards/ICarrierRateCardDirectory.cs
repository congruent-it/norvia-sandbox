using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Application.RateCards;

/// <summary>The TMS's read copy of the Carriers' Carrier Rate Cards (ADR 0013).</summary>
public interface ICarrierRateCardDirectory
{
    /// <summary>The Carrier's Carrier Rate Card, or null when it has none.</summary>
    CarrierRateCard? FindFor(CarrierId carrierId);
}
