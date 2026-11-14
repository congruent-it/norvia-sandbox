using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <summary>Where the Driver replacements the ERP sends are kept, on top of the seed file's first load.</summary>
public interface IDriverReplicas
{
    /// <summary>Keeps <paramref name="driver"/> as the copy of its ID, whole, replacing what was there.</summary>
    Task ReplaceAsync(Driver driver, CancellationToken cancellationToken);
}
