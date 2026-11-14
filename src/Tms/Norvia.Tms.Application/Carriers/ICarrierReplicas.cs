using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Application.Carriers;

/// <summary>Where the replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface ICarrierReplicas
{
    /// <summary>Keeps <paramref name="carrier"/> as the copy of its ID, whole, replacing what was there.</summary>
    Task ReplaceAsync(Carrier carrier, CancellationToken cancellationToken);
}
