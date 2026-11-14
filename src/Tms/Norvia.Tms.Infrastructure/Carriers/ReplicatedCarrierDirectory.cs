using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Carriers;

/// <summary>
/// The TMS's read copy of Carrier master data: the seed file's first load at Master-data revision 1, with every Carrier the ERP has
/// replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// </summary>
internal sealed class ReplicatedCarrierDirectory(SeedFileCarrierDirectory seed, IServiceScopeFactory scopes) : ICarrierDirectory, ICarrierReplicas
{
    public Carrier? Find(CarrierId id)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copy = context.Set<CarrierCopy>().AsNoTracking().SingleOrDefault(row => row.Id == id.Value);
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Carrier> ListActive()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<CarrierCopy>().AsNoTracking().ToList().Select(Of).ToDictionary(carrier => carrier.Id);
        return
        [
            .. seed.ListActive().Where(carrier => !copies.ContainsKey(carrier.Id)).Concat(copies.Values.Where(carrier => carrier.Active))
                .OrderBy(carrier => carrier.Name, StringComparer.Ordinal),
        ];
    }

    public async Task ReplaceAsync(Carrier carrier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<CarrierCopy>();
        var copy = await copies.SingleOrDefaultAsync(row => row.Id == carrier.Id.Value, cancellationToken);
        if (copy is not null && copy.Revision >= carrier.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        if (copy is null)
        {
            copy = new CarrierCopy { Id = carrier.Id.Value };
            copies.Add(copy);
        }

        copy.Name = carrier.Name;
        copy.Active = carrier.Active;
        copy.Revision = carrier.Revision;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Carrier Of(CarrierCopy copy) => new(new CarrierId(copy.Id), copy.Name, copy.Active, copy.Revision);
}
