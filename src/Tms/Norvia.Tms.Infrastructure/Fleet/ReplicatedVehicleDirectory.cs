using Ardalis.SharedKernel;
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
        var copy = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<VehicleCopy>>().FirstOrDefault(new VehicleCopyByIdSpec(id.Value));
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public IReadOnlyList<Vehicle> ListActive()
    {
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<VehicleCopy>>().List(new VehicleCopiesSpec()).Select(Of).ToDictionary(vehicle => vehicle.Id);
        return
        [
            .. seed.ListActive().Where(vehicle => !copies.ContainsKey(vehicle.Id)).Concat(copies.Values.Where(vehicle => vehicle.Active))
                .OrderBy(vehicle => vehicle.Registration, StringComparer.Ordinal),
        ];
    }

    public Task<bool> ReplaceAsync(Vehicle vehicle, CancellationToken cancellationToken) => ReplaceAsync(vehicle, retried: false, cancellationToken);

    private async Task<bool> ReplaceAsync(Vehicle vehicle, bool retried, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<IRepository<VehicleCopy>>();
        var copy = await copies.FirstOrDefaultAsync(new VehicleCopyForReplaceSpec(vehicle.Id.Value), cancellationToken);
        if (copy is not null && copy.Revision >= vehicle.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return false;
        }

        var isNew = copy is null;
        copy ??= new VehicleCopy { Id = vehicle.Id.Value };

        copy.Registration = vehicle.Registration;
        copy.Type = vehicle.Type.ToString();
        copy.PayloadKg = vehicle.PayloadKg;
        copy.PalletPlaces = vehicle.PalletPlaces;
        copy.Active = vehicle.Active;
        copy.Revision = vehicle.Revision;
        try
        {
            await SaveAsync(copies, copy, isNew, cancellationToken);
            return true;
        }
        catch (DbUpdateException) when (isNew && !retried)
        {
            // Another delivery inserted the same ID first: look again, and keep the higher revision.
            return await ReplaceAsync(vehicle, retried: true, cancellationToken);
        }
    }

    private static async Task SaveAsync(IRepository<VehicleCopy> copies, VehicleCopy copy, bool isNew, CancellationToken cancellationToken)
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

    private static Vehicle Of(VehicleCopy copy) =>
        new(new VehicleId(copy.Id), copy.Registration, Enum.Parse<VehicleType>(copy.Type), copy.PayloadKg, copy.PalletPlaces, copy.Active, copy.Revision);
}
