using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Driver master data: the seed file's first load at Master-data revision 1, with every Driver the ERP has
/// replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// A replacement changes nothing the TMS owns: Rosters, Absences and Trips stay as they are.
/// </summary>
internal sealed class ReplicatedDriverDirectory(SeedFileDriverDirectory seed, IServiceScopeFactory scopes) : IDriverDirectory, IDriverReplicas
{
    public Driver? Find(DriverId id)
    {
        using var scope = scopes.CreateScope();
        var copy = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<DriverCopy>>().FirstOrDefault(new DriverCopyByIdSpec(id.Value));
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Driver> ListActive()
    {
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<DriverCopy>>().List(new DriverCopiesSpec()).Select(Of).ToDictionary(driver => driver.Id);
        return
        [
            .. seed.ListActive().Where(driver => !copies.ContainsKey(driver.Id)).Concat(copies.Values.Where(driver => driver.Active))
                .OrderBy(driver => driver.Name, StringComparer.Ordinal),
        ];
    }

    public Task<bool> ReplaceAsync(Driver driver, CancellationToken cancellationToken) => ReplaceAsync(driver, retried: false, cancellationToken);

    private async Task<bool> ReplaceAsync(Driver driver, bool retried, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driver);
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<IRepository<DriverCopy>>();
        var copy = await copies.FirstOrDefaultAsync(new DriverCopyForReplaceSpec(driver.Id.Value), cancellationToken);
        if (copy is not null && copy.Revision >= driver.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return false;
        }

        var isNew = copy is null;
        copy ??= new DriverCopy { Id = driver.Id.Value };

        copy.Name = driver.Name;
        copy.Licences = string.Join(',', driver.Licences.Order());
        copy.Active = driver.Active;
        copy.Revision = driver.Revision;
        try
        {
            await SaveAsync(copies, copy, isNew, cancellationToken);
            return true;
        }
        catch (DbUpdateException) when (isNew && !retried)
        {
            // Another delivery inserted the same ID first: look again, and keep the higher revision.
            return await ReplaceAsync(driver, retried: true, cancellationToken);
        }
    }

    private static async Task SaveAsync(IRepository<DriverCopy> copies, DriverCopy copy, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            await copies.AddAsync(copy, cancellationToken);
        }
        else
        {
            await copies.UpdateAsync(copy, cancellationToken);
        }
    }

    private static Driver Of(DriverCopy copy) =>
        new(
            new DriverId(copy.Id),
            copy.Name,
            copy.Licences.Length == 0 ? new HashSet<LicenceCategory>() : copy.Licences.Split(',').Select(Enum.Parse<LicenceCategory>).ToHashSet(),
            copy.Active,
            copy.Revision);
}
