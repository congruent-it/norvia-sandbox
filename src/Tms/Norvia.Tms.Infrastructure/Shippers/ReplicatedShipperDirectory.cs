using Microsoft.EntityFrameworkCore;
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
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copy = context.Set<ShipperCopy>().AsNoTracking().SingleOrDefault(row => row.Id == id.Value);
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Shipper> List()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<ShipperCopy>().AsNoTracking().ToList().Select(Of).ToDictionary(shipper => shipper.Id);
        return [.. seed.List().Where(shipper => !copies.ContainsKey(shipper.Id)).Concat(copies.Values)];
    }

    public async Task ReplaceAsync(Shipper shipper, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipper);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<ShipperCopy>();
        var copy = await copies.SingleOrDefaultAsync(row => row.Id == shipper.Id.Value, cancellationToken);
        if (copy is null)
        {
            copy = new ShipperCopy { Id = shipper.Id.Value };
            copies.Add(copy);
        }

        copy.Name = shipper.Name;
        copy.Active = shipper.Active;
        copy.Revision = shipper.Revision;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Shipper Of(ShipperCopy copy) => new(new ShipperId(copy.Id), copy.Name, copy.Active, copy.Revision);
}
