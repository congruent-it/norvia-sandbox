using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// Which alert rules the estate builds for each preset in infra/presets (docs/operations/as-found.md). The Bicep is evaluated
/// locally with <c>bicep snapshot</c>, which predicts the resources a deployment would create; nothing is deployed.
/// </summary>
public sealed class AsFoundAlertsTests
{
    private static readonly string Root = RepositoryRoot.Path;

    // The estate's flows and queues as the estate hands them to the alerts module, plus one of each that no toggle ever lists.
    private static readonly string[] Entities = ["carrier-tracking", "unlisted"];

    [Fact]
    public void Fixed_BuildsOneFailedRunAlertPerFlow_AndOneDeadLetterAlertPerQueue()
    {
        var rules = Rules("fixed");

        rules.Select(rule => rule.Name).Should().BeEquivalentTo(
            "alert-failed-runs-carrier-tracking", "alert-failed-runs-unlisted",
            "alert-dead-letter-carrier-tracking", "alert-dead-letter-unlisted");
    }

    [Fact]
    public void AsFound_BuildsNoAlertForTheTrackingFlowAndQueue_AndOneForEveryOtherEntity()
    {
        var rules = Rules("as-found");

        rules.Select(rule => rule.Name).Should().BeEquivalentTo("alert-failed-runs-unlisted", "alert-dead-letter-unlisted");
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("as-found")]
    public void NoAlert_IsEverBuiltDisabled_AndEveryEntityHasAtMostOne(string preset)
    {
        var rules = Rules(preset);

        rules.Should().OnlyContain(rule => rule.Enabled, "a missing alert is an absent rule, never a disabled one");
        rules.Select(rule => rule.Name).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("as-found")]
    public void EveryAlert_SendsToTheActionGroup(string preset) =>
        Rules(preset).Should().OnlyContain(rule => rule.ActionGroups.Single().EndsWith("/actionGroups/ag", StringComparison.Ordinal));

    [Fact]
    public void Presets_HaveEveryToggle()
    {
        foreach (var preset in new[] { "fixed", "as-found" })
        {
            var toggles = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", $"{preset}.json")))!.AsObject();

            toggles.Select(toggle => toggle.Key).Should().BeEquivalentTo("flowsWithoutFailedRunAlert", "entitiesWithoutDeadLetterAlert", "noAlertReceiver");
        }
    }

    [Fact]
    public void NoAlertReceiver_BuildsTheActionGroupWithNoEmailReceiver()
    {
        var monitoring = File.ReadAllText(Path.Combine(Root, "infra", "estate", "monitoring.bicep"));

        monitoring.Should().Contain("emailReceivers: noAlertReceiver || empty(alertEmail) ? [] :");
    }

    private sealed record Rule(string Name, bool Enabled, string[] ActionGroups);

    private static List<Rule> Rules(string preset)
    {
        var directory = Directory.CreateTempSubdirectory("as-found-alerts-");
        try
        {
            var alerts = RelativeTo(directory.FullName, Path.Combine(Root, "infra", "estate", "alerts.bicep"));
            var presetFile = RelativeTo(directory.FullName, Path.Combine(Root, "infra", "presets", $"{preset}.json"));
            var flows = string.Join(", ", Entities.Select(entity => $"{{ name: '{entity}', logicAppName: 'logic-{entity}', workflowName: '{entity}' }}"));
            var queues = string.Join(", ", Entities.Select(entity => $"'{entity}'"));
            var parameters = Path.Combine(directory.FullName, "alerts.bicepparam");
            File.WriteAllText(parameters, $$"""
                using '{{alerts}}'
                param tags = {}
                param actionGroupId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Insights/actionGroups/ag'
                param asFound = json(loadTextContent('{{presetFile}}'))
                param serviceBusName = 'sb'
                param flows = [{{flows}}]
                param entities = [{{queues}}]
                """);

            Run(directory.FullName, "snapshot", "alerts.bicepparam", "--mode", "overwrite",
                "--subscription-id", "00000000-0000-0000-0000-000000000000", "--resource-group", "rg", "--location", "westeurope");

            var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(directory.FullName, "alerts.snapshot.json")))!;
            return snapshot["predictedResources"]!.AsArray()
                .Where(resource => resource!["type"]!.GetValue<string>() == "Microsoft.Insights/metricAlerts")
                .Select(resource => new Rule(
                    resource!["name"]!.GetValue<string>(),
                    resource["properties"]!["enabled"]!.GetValue<bool>(),
                    resource["properties"]!["actions"]!.AsArray().Select(action => action!["actionGroupId"]!.GetValue<string>()).ToArray()))
                .ToList();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // Bicep refuses an absolute path in a parameters file.
    private static string RelativeTo(string directory, string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');

    private static void Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(BicepPath(), arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, $"bicep {string.Join(' ', arguments)} printed: {output.Result}{error.Result}");
    }

    // `az bicep install` puts the Bicep CLI in ~/.azure/bin, which CI does too.
    private static string BicepPath()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure", "bin", OperatingSystem.IsWindows() ? "bicep.exe" : "bicep");
        return File.Exists(installed) ? installed : "bicep";
    }
}
