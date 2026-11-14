using System.Text.Json;
using Norvia.Erp.Application.MasterData.Load;
using Norvia.Erp.Domain.Drivers;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Infrastructure.Seed;

/// <summary>Reads the master data out of the sandbox's seed file: Shippers, Carriers, Rate Cards, Carrier Rate Cards, Vehicles and Drivers, with the IDs the TMS uses.</summary>
internal static class SeedFileReader
{
    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web);

    public static MasterDataSeed Read(Stream seedFile, string path)
    {
        ArgumentNullException.ThrowIfNull(seedFile);
        var seed = JsonSerializer.Deserialize<SeedFile>(seedFile, SeedJson)
            ?? throw new InvalidDataException($"The seed file '{path}' is empty.");

        return new MasterDataSeed(
            [.. (seed.Shippers ?? []).Select(shipper => new SeedShipper(shipper.Id, shipper.Name, shipper.Active))],
            [.. (seed.Carriers ?? []).Select(carrier => new SeedCarrier(carrier.Id, carrier.Name, carrier.Active))],
            [.. (seed.RateCards ?? []).Select(card => new SeedRateCard(card.Id, card.ShipperId, ToVersions(card.Versions, card.Id, path)))],
            [.. (seed.CarrierRateCards ?? []).Select(card => new SeedRateCard(card.Id, card.CarrierId, ToVersions(card.Versions, card.Id, path)))],
            [.. (seed.Vehicles ?? []).Select(vehicle => new SeedVehicle(
                vehicle.Id, vehicle.Registration, Enum.Parse<VehicleType>(vehicle.Type), vehicle.PayloadKg, vehicle.PalletPlaces, vehicle.Active))],
            [.. (seed.Drivers ?? []).Select(driver => new SeedDriver(driver.Id, driver.Name, [.. driver.Licences.Select(Enum.Parse<LicenceCategory>)], driver.Active))]);
    }

    private static List<RateCardVersion> ToVersions(IReadOnlyList<SeedVersion>? versions, string cardId, string path) =>
        [.. (versions ?? []).Select(version => new RateCardVersion(
            version.Version,
            version.ValidFrom,
            version.ValidTo,
            [.. version.Zones.Select(zone => new Zone(zone.Name, zone.Country, zone.PostcodePrefixes))],
            [.. version.Lanes.Select(lane => new Lane(
                lane.Origin,
                lane.Destination,
                lane.MinimumCharge ?? throw new InvalidDataException(
                    $"The Lane from {lane.Origin} to {lane.Destination} of {cardId} version {version.Version} in the seed file '{path}' has no minimum charge."),
                [.. lane.WeightBreaks.Select(weightBreak => new WeightBreak(weightBreak.UpToKg, weightBreak.PricePer100Kg))]))]))];

    private sealed record SeedFile(
        IReadOnlyList<SeedShipperRow>? Shippers,
        IReadOnlyList<SeedCarrierRow>? Carriers,
        IReadOnlyList<SeedRateCardRow>? RateCards,
        IReadOnlyList<SeedCarrierRateCardRow>? CarrierRateCards,
        IReadOnlyList<SeedVehicleRow>? Vehicles,
        IReadOnlyList<SeedDriverRow>? Drivers);

    private sealed record SeedShipperRow(string Id, string Name, bool Active);

    private sealed record SeedCarrierRow(string Id, string Name, bool Active);

    private sealed record SeedVehicleRow(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active);

    private sealed record SeedDriverRow(string Id, string Name, IReadOnlyList<string> Licences, bool Active);

    private sealed record SeedRateCardRow(string Id, string ShipperId, IReadOnlyList<SeedVersion>? Versions);

    private sealed record SeedCarrierRateCardRow(string Id, string CarrierId, IReadOnlyList<SeedVersion>? Versions);

    private sealed record SeedVersion(int Version, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<SeedZone> Zones, IReadOnlyList<SeedLane> Lanes);

    private sealed record SeedZone(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);

    private sealed record SeedLane(string Origin, string Destination, decimal? MinimumCharge, IReadOnlyList<SeedWeightBreak> WeightBreaks);

    private sealed record SeedWeightBreak(decimal UpToKg, decimal PricePer100Kg);
}
