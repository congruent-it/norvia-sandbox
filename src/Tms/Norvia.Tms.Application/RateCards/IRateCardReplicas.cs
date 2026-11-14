using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Application.RateCards;

/// <summary>Where the Rate Card replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface IRateCardReplicas
{
    /// <summary>Keeps <paramref name="rateCard"/> as the copy of its ID, whole, replacing its list of versions.</summary>
    Task ReplaceAsync(RateCard rateCard, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken);
}
