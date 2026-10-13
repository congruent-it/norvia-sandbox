using System.Text.Json;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Infrastructure.World;

/// <summary>
/// The Shippers the Simulator books for, loaded once from the seed file, the one source of master data (ADR 0002): each
/// active Shipper that has a Persona and a Rate Card, with the Zones and Lanes of its latest Rate Card version.
/// </summary>
internal sealed class SeedFileWorldSource : IWorldSource
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    private readonly SimulationWorld _world;

    public SeedFileWorldSource(IOptions<SeedOptions> options)
        : this(File.OpenRead(options.Value.Path))
    {
    }

    internal SeedFileWorldSource(Stream seedFile)
    {
        using (seedFile)
        {
            var seed = JsonSerializer.Deserialize<SeedFile>(seedFile, SeedJson) ?? throw new InvalidDataException("The seed file is empty.");
            _world = new SimulationWorld([.. seed.Shippers.Where(shipper => shipper.Active).Select(shipper => ToShipper(shipper, seed)).OfType<SimulatedShipper>()]);
        }
    }

    public SimulationWorld Load() => _world;

    private static SimulatedShipper? ToShipper(SeedShipper shipper, SeedFile seed)
    {
        var persona = seed.Personas.FirstOrDefault(candidate => candidate.Party.Type == "Shipper" && candidate.Party.Id == shipper.Id);
        var version = seed.RateCards?.FirstOrDefault(card => card.ShipperId == shipper.Id)?.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (persona is null || version is null)
        {
            return null;
        }

        return new SimulatedShipper(
            shipper.Id, shipper.Name, persona.Id, persona.DisplayName,
            [.. version.Zones.Select(zone => new RateZone(zone.Name, zone.Country, zone.PostcodePrefixes))],
            [.. version.Lanes.Select(lane => new RateLane(lane.Origin, lane.Destination))]);
    }

    private sealed record SeedFile(IReadOnlyList<SeedShipper> Shippers, IReadOnlyList<SeedPersona> Personas, IReadOnlyList<SeedRateCard>? RateCards);

    private sealed record SeedShipper(string Id, string Name, bool Active);

    private sealed record SeedPersona(string Id, string DisplayName, SeedParty Party);

    private sealed record SeedParty(string Type, string Id);

    private sealed record SeedRateCard(string ShipperId, IReadOnlyList<SeedRateCardVersion> Versions);

    private sealed record SeedRateCardVersion(int Version, IReadOnlyList<SeedZone> Zones, IReadOnlyList<SeedLane> Lanes);

    private sealed record SeedZone(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);

    private sealed record SeedLane(string Origin, string Destination);
}
