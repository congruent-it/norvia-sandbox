using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The as-found toggles for storage, secrets and retention (docs/operations/as-found.md): healthy gives the setting, and the toggle
/// removes only that setting. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed.
/// </summary>
public sealed class AsFoundStorageTests
{
    private static readonly string Root = RepositoryRoot.Path;

    private static readonly string[] BooleanToggles =
        ["noInboundLifecycleRule", "noTransitLifecycleRule", "proofFilesStayHot", "noMalwareScanning", "carrierSecretInlined", "appInsightsRetentionShort"];

    private const string InboundStorage = "infra/estate/inbound-storage.bicep";
    private const string DriverApp = "infra/estate/driver-app.bicep";
    private const string Monitoring = "infra/estate/monitoring.bicep";
    private const string Booking = "infra/estate/booking-logic-app.bicep";

    [Fact]
    public void Inbound_Healthy_HasALifecycleRuleThatDeletes_AndMalwareScanning()
    {
        var resources = Inbound();

        LifecycleRuleNames(resources).Should().Equal("inbound-proofs-of-delivery-expire");
        resources.Single(resource => Type(resource) == "Microsoft.Storage/storageAccounts/managementPolicies")["properties"]!.ToJsonString()
            .Should().Contain("\"delete\"").And.Contain("\"daysAfterModificationGreaterThan\":90");
        MalwareScanning(resources).Should().ContainSingle().Which["properties"]!["malwareScanning"]!["onUpload"]!["isEnabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void NoInboundLifecycleRule_RemovesOnlyTheInboundRule()
    {
        var healthy = Types(Inbound());
        var toggled = Types(Inbound("param noLifecycleRule = true"));

        healthy.Except(toggled).Should().Equal("Microsoft.Storage/storageAccounts/managementPolicies");
        toggled.Except(healthy).Should().BeEmpty();
    }

    [Fact]
    public void NoMalwareScanning_RemovesOnlyTheMalwareScanning()
    {
        var healthy = Types(Inbound());
        var toggled = Types(Inbound("param noMalwareScanning = true"));

        healthy.Except(toggled).Should().Equal("Microsoft.Security/defenderForStorageSettings");
        toggled.Except(healthy).Should().BeEmpty();
        LifecycleRuleNames(Inbound("param noMalwareScanning = true")).Should().Equal("inbound-proofs-of-delivery-expire");
    }

    [Fact]
    public void Transit_Healthy_HasALifecycleRule_AndNoTransitLifecycleRuleRemovesOnlyIt()
    {
        var healthy = Transit();
        var toggled = Transit("param noLifecycleRule = true");

        LifecycleRuleNames(healthy).Should().Equal("driver-proofs-of-delivery-expire");
        Types(healthy).Except(Types(toggled)).Should().Equal("Microsoft.Storage/storageAccounts/managementPolicies");
        Types(toggled).Except(Types(healthy)).Should().BeEmpty();
    }

    [Fact]
    public void AppInsightsRetentionShort_ShortensOnlyApplicationInsights()
    {
        var healthy = MonitoringResources();
        var toggled = MonitoringResources("param appInsightsRetentionShort = true");

        RetentionOf(healthy, "Microsoft.Insights/components", "RetentionInDays").Should().Be(90);
        RetentionOf(toggled, "Microsoft.Insights/components", "RetentionInDays").Should().BeLessThan(90);
        RetentionOf(toggled, "Microsoft.OperationalInsights/workspaces", "retentionInDays").Should().Be(90);
        Types(toggled).Should().Equal(Types(healthy));
    }

    [Fact]
    public void CarrierSecretInlined_AddsOneSettingWithTheCredential_AndNothingElseChanges()
    {
        var healthy = BookingSettings();
        var toggled = BookingSettings("param carrierSecretInlined = true");

        healthy.Keys.Should().NotContain("CARRIER_ENDPOINT_KEY");
        toggled.Keys.Except(healthy.Keys).Should().Equal("CARRIER_ENDPOINT_KEY");
        // The snapshot leaves the derivation unevaluated: it is the one the Carrier's secret in the vault has (partner-api.bicep).
        toggled["CARRIER_ENDPOINT_KEY"].Should().Contain("parameters('carrierEndpointKeySeed')").And.Contain("'CAR-0001'").And.Contain("'carrier-endpoint-key'");
        toggled.Where(setting => setting.Key != "CARRIER_ENDPOINT_KEY").Should().BeEquivalentTo(healthy);
        toggled["KEY_VAULT_URI"].Should().Be(healthy["KEY_VAULT_URI"], "the workflow still reads the credentials from Key Vault");
    }

    [Fact]
    public void LogicAppsWithoutTrackedProperties_LeavesThemOutOfTheListedWorkflowsOnly()
    {
        var healthy = Workflow("billing", listed: "[]");
        var toggled = Workflow("billing", listed: """["billing"]""");
        var other = Workflow("billing", listed: """["carrier-cancellation"]""");

        healthy.Should().Contain("trackedProperties");
        toggled.Should().NotContain("trackedProperties");
        other.Should().Be(healthy);
        JsonNode.Parse(toggled)!["definition"]!["actions"].Should().NotBeNull("only the tracked properties go");
    }

    [Fact]
    public void Presets_FixedHasEveryProtection_AsFoundIsAPartialSet()
    {
        var fixedPreset = Preset("fixed");
        var asFound = Preset("as-found");

        fixedPreset.Where(toggle => toggle.Value is JsonValue).Should().OnlyContain(toggle => !toggle.Value!.GetValue<bool>());
        fixedPreset["logicAppsWithoutTrackedProperties"]!.AsArray().Should().BeEmpty();
        var set = BooleanToggles
            .Select(name => asFound[name]!.GetValue<bool>()).ToList();
        set.Should().Contain(true).And.Contain(false);
        asFound["logicAppsWithoutTrackedProperties"]!.AsArray().Should().NotBeEmpty();
    }

    private static List<JsonNode> Inbound(params string[] toggles) => BicepSnapshot.Resources(InboundStorage,
        ["param location = 'westeurope'", "param tags = {}", "param integrationRetentionDays = 90",
            "param apiManagementPrincipalId = '00000000-0000-0000-0000-000000000001'", "param proofOfDeliveryPrincipalId = '00000000-0000-0000-0000-000000000002'", .. toggles]);

    private static List<JsonNode> Transit(params string[] toggles) => BicepSnapshot.Resources(DriverApp,
        ["param location = 'westeurope'", "param tags = {}", "param integrationRetentionDays = 90", "param endpointKeySeed = 'seed'",
            "param driverAppPrincipalId = '00000000-0000-0000-0000-000000000001'", "param driverReportsPrincipalId = '00000000-0000-0000-0000-000000000002'",
            "param tripDispatchPrincipalId = '00000000-0000-0000-0000-000000000003'", .. toggles]);

    private static List<JsonNode> MonitoringResources(params string[] toggles) => BicepSnapshot.Resources(Monitoring,
        ["param location = 'westeurope'", "param tags = {}", "param integrationRetentionDays = 90", "param alertEmail = ''", "param noAlertReceiver = false",
            "param integrationConsolePrincipalId = '00000000-0000-0000-0000-000000000001'", .. toggles]);

    private static Dictionary<string, string> BookingSettings(params string[] toggles)
    {
        var resources = BicepSnapshot.Resources(Booking,
            ["param location = 'westeurope'", "param tags = {}", "param integrationRetentionDays = 90", "param bookingIdentityName = 'id-booking'",
                "param tmsApiClientId = 'tms'", "param tmsBaseUrl = 'https://tms'", "param partnerPortalBaseUrl = 'https://portal'",
                "param applicationInsightsName = 'appi'", "param serviceBusName = 'sb'", "param planName = 'plan'", "param contentKeyVaultName = 'kv-content'",
                "param carrierKeyVaultName = 'kv-carrier'", "param carrierEndpointKeySeed = 'seed'",
                "param integrationConsolePrincipalId = '00000000-0000-0000-0000-000000000001'", .. toggles]);

        return resources.Single(resource => Type(resource) == "Microsoft.Web/sites")["properties"]!["siteConfig"]!["appSettings"]!.AsArray()
            .ToDictionary(setting => setting!["name"]!.GetValue<string>(), setting => setting!["value"]!.GetValue<string>());
    }

    // What infra/stage-logic-app.ps1 puts in the zip for the workflow, given the toggle.
    private static string Workflow(string flow, string listed)
    {
        var directory = Directory.CreateTempSubdirectory("stage-logic-app-");
        try
        {
            var zip = Path.Combine(directory.FullName, "app.zip");
            var script = Path.Combine(Root, "infra", "stage-logic-app.ps1");
            var start = new System.Diagnostics.ProcessStartInfo("pwsh", ["-NoProfile", "-File", script, Path.Combine(Root, "src", "Integration", "BillingLogicApp"), zip, "host.json", flow])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["ESTATE_ASFOUND"] = $$"""{"logicAppsWithoutTrackedProperties":{{listed}}}""";
            using var process = System.Diagnostics.Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.Should().Be(0, error);

            using var archive = ZipFile.OpenRead(zip);
            using var reader = new StreamReader(archive.GetEntry($"{flow}/workflow.json")!.Open());
            return reader.ReadToEnd();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static JsonObject Preset(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", $"{name}.json")))!.AsObject();

    private static string Type(JsonNode resource) => resource["type"]!.GetValue<string>();

    private static List<string> Types(List<JsonNode> resources) => resources.Select(Type).Distinct().Order().ToList();

    private static List<JsonNode> MalwareScanning(List<JsonNode> resources) => resources.Where(resource => Type(resource) == "Microsoft.Security/defenderForStorageSettings").ToList();

    private static List<string> LifecycleRuleNames(List<JsonNode> resources) => resources
        .Where(resource => Type(resource) == "Microsoft.Storage/storageAccounts/managementPolicies")
        .SelectMany(resource => resource["properties"]!["policy"]!["rules"]!.AsArray().Select(rule => rule!["name"]!.GetValue<string>()))
        .ToList();

    private static int RetentionOf(List<JsonNode> resources, string type, string property) =>
        resources.Single(resource => Type(resource) == type)["properties"]![property]!.GetValue<int>();
}
