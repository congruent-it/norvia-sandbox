using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>Where the Vehicle replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface IVehicleReplicas
{
    /// <summary>Keeps <paramref name="vehicle"/> as the copy of its ID, whole, replacing what was there.</summary>
    /// <returns>False when the copy already held this revision or a later one, which a delivery that lost a race with a later one finds, and nothing was changed.</returns>
    Task<bool> ReplaceAsync(Vehicle vehicle, CancellationToken cancellationToken);
}
