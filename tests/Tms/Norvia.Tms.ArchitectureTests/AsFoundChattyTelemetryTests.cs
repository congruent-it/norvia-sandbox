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
    public void EveryPreset_HasTheToggle_AndItIsOnInIncident2Alone(string preset)
    {
        var toggles = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", $"{preset}.json")))!;

        toggles["chattyTelemetry"].Should().NotBeNull(preset);
        toggles["chattyTelemetry"]!.GetValue<bool>().Should().Be(preset == "incident-2", preset);
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
    public void TheCostSnapshots_HaveTheSameVolume_SoTheToggleIsTheOnlyDifference() =>
        CostSnapshot("fixed").Days.Select(day => day.TransportOrders).Should().Equal(CostSnapshot("chatty-telemetry").Days.Select(day => day.TransportOrders));

    [Fact]
    public void TheFixedCostSnapshot_IsFlat()
    {
        var days = CostSnapshot("fixed").Days;

        days.Max(day => day.Workspace).Should().BeLessThan(days.Min(day => day.Workspace) * 1.5);
    }

    [Fact]
    public void TheChattyCostSnapshot_HasTheWorkspaceRiseWhileVolumeStaysFlat()
    {
        var chatty = CostSnapshot("chatty-telemetry");
        var before = chatty.Days.Where(day => day.Date < chatty.ToggleOnFrom).ToList();

        before.Should().NotBeEmpty();
        chatty.Days.Where(day => day.Date >= chatty.ToggleOnFrom).Min(day => day.Workspace).Should().BeGreaterThan(before.Max(day => day.Workspace) * 3);
    }

    [Fact]
    public void TheChattyCostSnapshot_HasNoOtherResourceExplainTheRise()
    {
        var chatty = CostSnapshot("chatty-telemetry");

        chatty.Days.Where(day => day.Date >= chatty.ToggleOnFrom).Average(day => day.OtherSpend)
            .Should().BeApproximately(chatty.Days.Where(day => day.Date < chatty.ToggleOnFrom).Average(day => day.OtherSpend), 0.5);
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
