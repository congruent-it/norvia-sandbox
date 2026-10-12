using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.DriverSignIns;
using Norvia.PartnerPortal.Infrastructure.Personas;

namespace Norvia.PartnerPortal.Infrastructure.DriverSignIns;

/// <summary>
/// The active Drivers of the seed file, the one source of Drivers, with the Entra test user the estate made for each: the
/// Driver ID lowercased, at the estate's domain (infra/create-driver-test-users.sh).
/// </summary>
internal sealed class SeedFileDriverSignInDirectory : IDriverSignInDirectory
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    public SeedFileDriverSignInDirectory(IOptions<SeedOptions> seed, IOptions<DriverSignInOptions> signIn)
        : this(File.OpenRead(seed.Value.Path), signIn.Value)
    {
    }

    internal SeedFileDriverSignInDirectory(Stream seedFile, DriverSignInOptions signIn)
    {
        using (seedFile)
        {
            var seed = JsonSerializer.Deserialize<SeedFile>(seedFile, SeedJson) ?? throw new InvalidDataException("The seed file is empty.");
            var hasUsers = !string.IsNullOrWhiteSpace(signIn.Domain);
            Current = new DriverSignInsView(
                hasUsers ? signIn.Password : null,
                [.. seed.Drivers.Where(driver => driver.Active)
                    .Select(driver => new DriverSignInView(driver.Id, driver.Name, hasUsers ? $"{driver.Id.ToLowerInvariant()}@{signIn.Domain}" : null))]);
        }
    }

    public DriverSignInsView Current { get; }

    private sealed record SeedFile(IReadOnlyList<SeedDriver> Drivers);

    private sealed record SeedDriver(string Id, string Name, bool Active);
}
