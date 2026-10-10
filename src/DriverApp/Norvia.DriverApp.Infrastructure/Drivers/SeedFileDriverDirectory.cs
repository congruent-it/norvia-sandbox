using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.DriverApp.Application.Drivers;

namespace Norvia.DriverApp.Infrastructure.Drivers;

/// <summary>The active Drivers of the seed file, the one source of Drivers, loaded once. Only the ID and name are the Driver app's business.</summary>
internal sealed class SeedFileDriverDirectory : IDriverDirectory
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    public SeedFileDriverDirectory(IOptions<SeedOptions> options)
        : this(File.OpenRead(options.Value.Path))
    {
    }

    internal SeedFileDriverDirectory(Stream seedFile)
    {
        using (seedFile)
        {
            var seed = JsonSerializer.Deserialize<SeedFile>(seedFile, SeedJson) ?? throw new InvalidDataException("The seed file is empty.");
            All = [.. seed.Drivers.Where(driver => driver.Active).Select(driver => new DriverView(driver.Id, driver.Name))];
        }
    }

    public IReadOnlyList<DriverView> All { get; }

    private sealed record SeedFile(IReadOnlyList<SeedDriver> Drivers);

    private sealed record SeedDriver(string Id, string Name, bool Active);
}
