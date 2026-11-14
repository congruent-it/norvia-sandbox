using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Vehicle master data: the seed file's first load at Master-data revision 1, with every Vehicle the ERP has
/// replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// A replacement changes nothing the TMS owns: Off the road days and Trips stay as they are.
/// </summary>
internal sealed class ReplicatedVehicleDirectory(SeedFileVehicleDirectory seed, IServiceScopeFactory scopes) : IVehicleDirectory, IVehicleReplicas
{
    public Vehicle? Find(VehicleId id)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copy = context.Set<VehicleCopy>().AsNoTracking().SingleOrDefault(row => row.Id == id.Value);
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Vehicle> ListActive()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<VehicleCopy>().AsNoTracking().ToList().Select(Of).ToDictionary(vehicle => vehicle.Id);
        return
        [
            .. seed.ListActive().Where(vehicle => !copies.ContainsKey(vehicle.Id)).Concat(copies.Values.Where(vehicle => vehicle.Active))
                .OrderBy(vehicle => vehicle.Registration, StringComparer.Ordinal),
        ];
    }

    public async Task ReplaceAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<VehicleCopy>();
        var copy = await copies.SingleOrDefaultAsync(row => row.Id == vehicle.Id.Value, cancellationToken);
        if (copy is not null && copy.Revision >= vehicle.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        if (copy is null)
        {
            copy = new VehicleCopy { Id = vehicle.Id.Value };
            copies.Add(copy);
        }

        copy.Registration = vehicle.Registration;
        copy.Type = vehicle.Type.ToString();
        copy.PayloadKg = vehicle.PayloadKg;
        copy.PalletPlaces = vehicle.PalletPlaces;
        copy.Active = vehicle.Active;
        copy.Revision = vehicle.Revision;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Vehicle Of(VehicleCopy copy) =>
        new(new VehicleId(copy.Id), copy.Registration, Enum.Parse<VehicleType>(copy.Type), copy.PayloadKg, copy.PalletPlaces, copy.Active, copy.Revision);
}
