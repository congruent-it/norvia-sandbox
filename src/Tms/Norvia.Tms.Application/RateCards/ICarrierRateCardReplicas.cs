using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Application.RateCards;

/// <summary>Where the Carrier Rate Card replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface ICarrierRateCardReplicas
{
    /// <summary>Keeps <paramref name="carrierRateCard"/> as the copy of its ID, whole, replacing its list of versions.</summary>
    Task ReplaceAsync(CarrierRateCard carrierRateCard, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken);
}
