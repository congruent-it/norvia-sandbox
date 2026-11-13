using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.Shippers;

/// <summary>Where the replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface IShipperReplicas
{
    /// <summary>Keeps <paramref name="shipper"/> as the copy of its ID, whole, replacing what was there.</summary>
    Task ReplaceAsync(Shipper shipper, CancellationToken cancellationToken);
}
