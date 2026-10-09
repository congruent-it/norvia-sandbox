using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The TMS's read copy of Driver master data, loaded once from the seed file until the ERP sync exists (ADR 0014). The
/// TMS never edits it.
/// </summary>
internal sealed class SeedFileDriverDirectory : IDriverDirectory
{
    private readonly IReadOnlyList<Driver> _active;

    public SeedFileDriverDirectory(IOptions<SeedOptions> options)
    {
        _active =
        [
            .. (SeedFileReader.Read<SeedFile>(options).Drivers ?? [])
                .Where(driver => driver.Active)
                .Select(driver => new Driver(
                    new DriverId(driver.Id), driver.Name, driver.Licences.Select(Enum.Parse<LicenceCategory>).ToHashSet(), driver.Active))
                .OrderBy(driver => driver.Name, StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<Driver> ListActive() => _active;

    private sealed record SeedFile(IReadOnlyList<SeedDriver>? Drivers);

    private sealed record SeedDriver(string Id, string Name, IReadOnlyList<string> Licences, bool Active);
}
