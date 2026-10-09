using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Vehicle master data, loaded once from the seed file until the ERP sync exists (ADR 0014). The
/// TMS never edits it.
/// </summary>
internal sealed class SeedFileVehicleDirectory : IVehicleDirectory
{
    private readonly IReadOnlyList<Vehicle> _active;

    public SeedFileVehicleDirectory(IOptions<SeedOptions> options)
    {
        _active =
        [
            .. (SeedFileReader.Read<SeedFile>(options).Vehicles ?? [])
                .Where(vehicle => vehicle.Active)
                .Select(vehicle => new Vehicle(
                    new VehicleId(vehicle.Id), vehicle.Registration, Enum.Parse<VehicleType>(vehicle.Type), vehicle.PayloadKg, vehicle.PalletPlaces, vehicle.Active))
                .OrderBy(vehicle => vehicle.Registration, StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<Vehicle> ListActive() => _active;

    private sealed record SeedFile(IReadOnlyList<SeedVehicle>? Vehicles);

    private sealed record SeedVehicle(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active);
}
