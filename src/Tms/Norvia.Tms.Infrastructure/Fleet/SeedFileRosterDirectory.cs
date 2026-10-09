using System.Globalization;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The Drivers' Rosters, loaded once from the seed file until the Planner can edit them (P10). The TMS owns Rosters, but
/// the seed file gives the demo its starting ones, next to the Driver they belong to.
/// </summary>
internal sealed class SeedFileRosterDirectory : IRosterDirectory
{
    private readonly IReadOnlyList<Roster> _rosters;

    public SeedFileRosterDirectory(IOptions<SeedOptions> options)
    {
        _rosters =
        [
            .. (SeedFileReader.Read<SeedFile>(options).Drivers ?? [])
                .Where(driver => driver.Roster is { Count: > 0 })
                .Select(driver => new Roster(
                    new DriverId(driver.Id),
                    driver.Roster!.ToDictionary(
                        weekday => Enum.Parse<DayOfWeek>(weekday.Key),
                        weekday => new ShiftTimes(Time(weekday.Value.Start), Time(weekday.Value.End))))),
        ];
    }

    public IReadOnlyList<Roster> List() => _rosters;

    private static TimeOnly Time(string text) => TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);

    private sealed record SeedFile(IReadOnlyList<SeedDriver>? Drivers);

    private sealed record SeedDriver(string Id, IReadOnlyDictionary<string, SeedShift>? Roster);

    private sealed record SeedShift(string Start, string End);
}
