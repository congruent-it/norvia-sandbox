using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Infrastructure.Shippers;

/// <summary>
/// The TMS's read copy of Shipper master data, loaded once from the seed file until the ERP sync exists (ADR 0002).
/// </summary>
internal sealed class SeedFileShipperDirectory : IShipperDirectory
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<ShipperId, Shipper> _shippers;

    public SeedFileShipperDirectory(IOptions<SeedOptions> options)
    {
        using var file = File.OpenRead(options.Value.Path);
        var seed = JsonSerializer.Deserialize<SeedFile>(file, SeedJson)
            ?? throw new InvalidDataException($"The seed file '{options.Value.Path}' is empty.");

        _shippers = seed.Shippers
            .Select(shipper => new Shipper(new ShipperId(shipper.Id), shipper.Name, shipper.Active))
            .ToDictionary(shipper => shipper.Id);
    }

    public Shipper? Find(ShipperId id) => _shippers.GetValueOrDefault(id);

    private sealed record SeedFile(IReadOnlyList<SeedShipper> Shippers);

    private sealed record SeedShipper(string Id, string Name, bool Active);
}
