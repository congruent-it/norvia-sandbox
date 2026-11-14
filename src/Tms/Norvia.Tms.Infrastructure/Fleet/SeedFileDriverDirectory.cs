using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Driver master data as the seed file first loads it, at Master-data revision 1. The TMS never edits it; the
/// ERP's replacements are laid over it by <see cref="ReplicatedDriverDirectory"/> (ADR 0014, E3).
/// </summary>
internal sealed class SeedFileDriverDirectory : IDriverDirectory
{
    private readonly IReadOnlyDictionary<DriverId, Driver> _all;
    private readonly IReadOnlyList<Driver> _active;

    public SeedFileDriverDirectory(IOptions<SeedOptions> options)
    {
        var all = (SeedFileReader.Read<SeedFile>(options).Drivers ?? [])
            .Select(driver => new Driver(
                new DriverId(driver.Id), driver.Name, driver.Licences.Select(Enum.Parse<LicenceCategory>).ToHashSet(), driver.Active))
            .ToList();
        _all = all.ToDictionary(driver => driver.Id);
        _active = [.. all.Where(driver => driver.Active).OrderBy(driver => driver.Name, StringComparer.Ordinal)];
    }

    public IReadOnlyList<Driver> ListActive() => _active;

    public Driver? Find(DriverId id) => _all.GetValueOrDefault(id);

    private sealed record SeedFile(IReadOnlyList<SeedDriver>? Drivers);

    private sealed record SeedDriver(string Id, string Name, IReadOnlyList<string> Licences, bool Active);
}
