using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The <c>chattyTelemetry</c> spend toggle (docs/operations/as-found.md): healthy by default, in every preset and in the template's default,
/// and applied to the running applications by infra/as-found.ps1. The AppHost model tests check that it sets the applications' log level.
/// </summary>
public sealed class AsFoundChattyTelemetryTests
{
    private static readonly string Root = RepositoryRoot.Path;

    public static TheoryData<string> Presets => [.. Directory.GetFiles(Path.Combine(Root, "infra", "presets"), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()];

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPreset_HasTheToggle_AndItIsOffBecauseTheCostCheckIsNotBuiltYet(string preset)
    {
        var toggles = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", $"{preset}.json")))!;

        toggles["chattyTelemetry"].Should().NotBeNull(preset);
        toggles["chattyTelemetry"]!.GetValue<bool>().Should().BeFalse(preset);
    }

    [Fact]
    public void TheEstateTemplate_IsHealthyByDefault()
    {
        var bicepparam = File.ReadAllText(Path.Combine(Root, "infra", "estate.bicepparam"));

        bicepparam.Should().Contain("\"chattyTelemetry\":false");
    }

    [Fact]
    public void TheAsFoundScript_AppliesTheLogLevelToTheRunningApplications()
    {
        var script = File.ReadAllText(Path.Combine(Root, "infra", "as-found.ps1"));

        script.Should().Contain("chattyTelemetry").And.Contain("Logging__LogLevel__Default");
    }

    [Fact]
    public void TheCostSnapshots_ShowTheShapeOfTheToggle_AWorkspaceThatRisesWhileVolumeStaysFlat()
    {
        var fixedEstate = CostSnapshot("fixed");
        var chatty = CostSnapshot("chatty-telemetry");

        fixedEstate.Days.Select(day => day.TransportOrders).Should().Equal(chatty.Days.Select(day => day.TransportOrders), "volume is flat and the same, so the toggle is the only difference");
        fixedEstate.Days.Max(day => day.Workspace).Should().BeLessThan(fixedEstate.Days.Min(day => day.Workspace) * 1.5, "the fixed estate's spend is flat");

        var on = chatty.Days.Where(day => day.Date >= chatty.ToggleOnFrom).ToList();
        var before = chatty.Days.Where(day => day.Date < chatty.ToggleOnFrom).ToList();
        before.Should().NotBeEmpty();
        on.Min(day => day.Workspace).Should().BeGreaterThan(before.Max(day => day.Workspace) * 3, "the workspace's spend rises while the volume stays where it was");
        on.Average(day => day.OtherSpend).Should().BeApproximately(before.Average(day => day.OtherSpend), 0.5, "no other resource explains the rise");
    }

    private static CostShape CostSnapshot(string name)
    {
        var path = Path.Combine(Root, "src", "HealthCheck", "Norvia.HealthCheck.Infrastructure", "Snapshots", $"{name}.cost-snapshot.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["measured"]!.GetValue<bool>().Should().BeFalse("the snapshot is modelled until an Azure deployment measures it, and says so");
        var days = json["days"]!.AsArray().Select(day => new CostDay(
            DateOnly.Parse(day!["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
            day["transportOrders"]!.GetValue<int>(),
            day["spendByResource"]!["log-workspace"]!.GetValue<double>(),
            day["spendByResource"]!.AsObject().Where(resource => resource.Key is not "log-workspace" and not "app-insights").Sum(resource => resource.Value!.GetValue<double>()))).ToList();
        var from = json["toggleOnFrom"]?.GetValue<string>();

        return new CostShape(days, from is null ? DateOnly.MaxValue : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed record CostDay(DateOnly Date, int TransportOrders, double Workspace, double OtherSpend);

    private sealed record CostShape(List<CostDay> Days, DateOnly ToggleOnFrom);
}
