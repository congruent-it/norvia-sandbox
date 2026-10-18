using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.Trips;

/// <summary>
/// A TMS with the controlled clock whose seed also has a Vehicle that needs a CE licence, which Joost Bakker (B and C)
/// doesn't hold, so a Trip can break LicenceNotHeld. It starts from a copy of the test seed.
/// </summary>
public sealed class TripsTmsFixture : ControlledClockTmsFixture
{
    public const string TractorId = "VEH-0003";

    private readonly string _seedPath = Path.Combine(Path.GetTempPath(), $"tms.trips-seed.{Guid.NewGuid():N}.json");

    protected override Dictionary<string, string> TmsApiSettings()
    {
        var seed = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json")))!.AsObject();
        seed["vehicles"]!.AsArray().Add(new JsonObject
        {
            ["id"] = TractorId,
            ["registration"] = "TT-301-F",
            ["type"] = "TractorWithTrailer",
            ["payloadKg"] = 24000,
            ["palletPlaces"] = 33,
            ["active"] = true,
        });
        File.WriteAllText(_seedPath, seed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var settings = base.TmsApiSettings();
        settings["Seed__Path"] = _seedPath;
        return settings;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_seedPath);
        GC.SuppressFinalize(this);
    }
}
