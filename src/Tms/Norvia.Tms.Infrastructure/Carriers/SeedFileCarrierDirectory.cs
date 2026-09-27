using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Carriers;

/// <summary>
/// The TMS's read copy of Carrier master data, loaded once from the seed file until the ERP sync exists (ADR 0002). The
/// TMS never edits it.
/// </summary>
internal sealed class SeedFileCarrierDirectory : ICarrierDirectory
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<CarrierId, Carrier> _carriers;

    public SeedFileCarrierDirectory(IOptions<SeedOptions> options)
    {
        using var file = File.OpenRead(options.Value.Path);
        var seed = JsonSerializer.Deserialize<SeedFile>(file, SeedJson)
            ?? throw new InvalidDataException($"The seed file '{options.Value.Path}' is empty.");

        _carriers = (seed.Carriers ?? [])
            .Select(carrier => new Carrier(new CarrierId(carrier.Id), carrier.Name, carrier.Active))
            .ToDictionary(carrier => carrier.Id);
    }

    public Carrier? Find(CarrierId id) => _carriers.GetValueOrDefault(id);

    private sealed record SeedFile(IReadOnlyList<SeedCarrier>? Carriers);

    private sealed record SeedCarrier(string Id, string Name, bool Active);
}
