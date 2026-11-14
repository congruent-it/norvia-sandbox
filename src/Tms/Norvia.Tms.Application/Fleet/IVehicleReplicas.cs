using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>Where the Vehicle replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface IVehicleReplicas
{
    /// <summary>Keeps <paramref name="vehicle"/> as the copy of its ID, whole, replacing what was there.</summary>
    Task ReplaceAsync(Vehicle vehicle, CancellationToken cancellationToken);
}
