using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Shippers;

/// <summary>
/// The TMS's read copy of Shipper master data: the seed file's first load at Master-data revision 1, with every Shipper the ERP has
/// replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// </summary>
internal sealed class ReplicatedShipperDirectory(SeedFileShipperDirectory seed, IServiceScopeFactory scopes) : IShipperDirectory, IShipperReplicas
{
    public Shipper? Find(ShipperId id)
    {
        using var scope = scopes.CreateScope();
        var copy = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<ShipperCopy>>().FirstOrDefault(new ShipperCopyByIdSpec(id.Value));
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Shipper> List()
    {
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<ShipperCopy>>().List(new ShipperCopiesSpec()).Select(Of).ToDictionary(shipper => shipper.Id);
        return [.. seed.List().Where(shipper => !copies.ContainsKey(shipper.Id)).Concat(copies.Values)];
    }

    public async Task ReplaceAsync(Shipper shipper, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipper);
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<IRepository<ShipperCopy>>();
        var copy = await copies.FirstOrDefaultAsync(new ShipperCopyForReplaceSpec(shipper.Id.Value), cancellationToken);
        if (copy is not null && copy.Revision >= shipper.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        var isNew = copy is null;
        copy ??= new ShipperCopy { Id = shipper.Id.Value };

        copy.Name = shipper.Name;
        copy.Active = shipper.Active;
        copy.Revision = shipper.Revision;
        await SaveAsync(copies, copy, isNew, cancellationToken);
    }

    private static async Task SaveAsync(IRepository<ShipperCopy> copies, ShipperCopy copy, bool isNew, CancellationToken cancellationToken)
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

    private static Shipper Of(ShipperCopy copy) => new(new ShipperId(copy.Id), copy.Name, copy.Active, copy.Revision);
}
