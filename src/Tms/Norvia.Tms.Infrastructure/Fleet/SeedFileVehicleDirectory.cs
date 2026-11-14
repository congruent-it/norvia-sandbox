using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Vehicle master data as the seed file first loads it, at Master-data revision 1. The TMS never edits it; the
/// ERP's replacements are laid over it by <see cref="ReplicatedVehicleDirectory"/> (ADR 0014, E3).
/// </summary>
internal sealed class SeedFileVehicleDirectory : IVehicleDirectory
{
    private readonly IReadOnlyDictionary<VehicleId, Vehicle> _all;
    private readonly IReadOnlyList<Vehicle> _active;

    public SeedFileVehicleDirectory(IOptions<SeedOptions> options)
    {
        var all = (SeedFileReader.Read<SeedFile>(options).Vehicles ?? [])
            .Select(vehicle => new Vehicle(
                new VehicleId(vehicle.Id), vehicle.Registration, Enum.Parse<VehicleType>(vehicle.Type), vehicle.PayloadKg, vehicle.PalletPlaces, vehicle.Active))
            .ToList();
        _all = all.ToDictionary(vehicle => vehicle.Id);
        _active = [.. all.Where(vehicle => vehicle.Active).OrderBy(vehicle => vehicle.Registration, StringComparer.Ordinal)];
    }

    public IReadOnlyList<Vehicle> ListActive() => _active;

    public Vehicle? Find(VehicleId id) => _all.GetValueOrDefault(id);

    private sealed record SeedFile(IReadOnlyList<SeedVehicle>? Vehicles);

    private sealed record SeedVehicle(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active);
}
