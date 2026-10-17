using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.TestSupport;

/// <summary>
/// A TMS of its own whose seed a test can correct, as the ERP will correct a Rate Card. The TMS doesn't reload Rate
/// Cards while it runs, so a correction restarts it. It starts from a copy of the test seed.
/// </summary>
public sealed class CorrectableSeedTmsFixture : TmsFixture
{
    private readonly string _seedPath = Path.Combine(Path.GetTempPath(), $"tms.correctable-seed.{Guid.NewGuid():N}.json");

    protected override Dictionary<string, string> TmsApiSettings()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json"), _seedPath, overwrite: true);
        var settings = base.TmsApiSettings();
        settings["Seed__Path"] = _seedPath;
        return settings;
    }

    /// <summary>Changes the TMS's seed, then restarts the TMS so it loads it.</summary>
    public async Task CorrectSeedAsync(Action<JsonObject> correct, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correct);
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(_seedPath, cancellationToken))!.AsObject();
        correct(seed);
        await File.WriteAllTextAsync(_seedPath, seed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        await RestartTmsAsync(cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_seedPath);
    }
}
