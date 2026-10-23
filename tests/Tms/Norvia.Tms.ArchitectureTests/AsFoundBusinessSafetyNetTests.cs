using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The as-found toggles for the API Management policies and the Driver reports Logic App (docs/operations/as-found.md): healthy gives the
/// rate limit and no cache, and each toggle changes only its own setting. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing
/// is deployed. The TMS settings the other three toggles flow into are tested in the AppHost model tests and the Infrastructure unit tests.
/// </summary>
public sealed class AsFoundBusinessSafetyNetTests
{
    private static readonly string Root = RepositoryRoot.Path;

    private const string PartnerApi = "infra/estate/partner-api.bicep";
    private static readonly string[] Reads = ["list-shipper-transport-orders", "find-shipper-transport-order", "get-shipper-transport-order"];
    private const string ProofFile = "get-shipper-proof-of-delivery-file";

    [Fact]
    public void Healthy_EveryTrackingOperationHasTheRateLimit_AndNoneCaches()
    {
        var policies = Policies();

        policies.Keys.Should().BeEquivalentTo([.. Reads, ProofFile]);
        policies.Values.Should().OnlyContain(policy => policy.Contains("<rate-limit-by-key calls=\"120\""));
        policies.Values.Should().OnlyContain(policy => !policy.Contains("cache-"));
        policies.Values.Should().OnlyContain(policy => !policy.Contains("[["), "every place is filled or emptied");
    }

    [Fact]
    public void NoShipperRateLimit_RemovesOnlyTheRateLimit()
    {
        var healthy = Policies();
        var toggled = Policies("param noShipperRateLimit = true");

        toggled.Values.Should().OnlyContain(policy => !policy.Contains("rate-limit"));
        foreach (var (operation, policy) in toggled)
        {
            Remove(healthy[operation], RateLimit).Should().Be(policy, $"{operation} changes only by the rate limit");
        }
    }

    [Fact]
    public void ShipperTrackingCached_AddsALongLivedCachePerShipperToTheReadsOnly_AndKeepsTheRateLimit()
    {
        var healthy = Policies();
        var toggled = Policies("param shipperTrackingCached = true");

        foreach (var operation in Reads)
        {
            toggled[operation].Should().Contain("cache-lookup-value").And.Contain("cache-store-value").And.Contain("duration=\"86400\"")
                .And.Contain("shipperId", "the key names the Shipper, so no Shipper is given another's answer")
                .And.Contain("<rate-limit-by-key");
            toggled[operation].IndexOf("cache-store-value", StringComparison.Ordinal).Should().BeGreaterThan(toggled[operation].IndexOf("<outbound>", StringComparison.Ordinal),
                "the answer is kept once it has been mapped for the Shipper");
            toggled[operation].IndexOf("cache-lookup-value", StringComparison.Ordinal).Should().BeLessThan(toggled[operation].IndexOf("<backend>", StringComparison.Ordinal));
        }
        toggled[ProofFile].Should().Be(healthy[ProofFile], "a file is streamed, never cached");
    }

    [Fact]
    public void ShipperTrackingCached_RemovesNothingFromThePolicies()
    {
        var healthy = Policies();
        var toggled = Policies("param shipperTrackingCached = true");

        foreach (var operation in Reads)
        {
            Remove(toggled[operation], CacheLookup, CacheStore).Should().Be(healthy[operation]);
        }
    }

    [Fact]
    public void BothToggles_AreIndependent_AndEveryPolicyStaysWellFormedXml()
    {
        var both = Policies("param shipperTrackingCached = true", "param noShipperRateLimit = true");
        foreach (var policy in new[] { both, Policies(), Policies("param shipperTrackingCached = true") }.SelectMany(policies => policies.Values))
        {
            System.Xml.Linq.XDocument.Parse(policy).Root!.Name.LocalName.Should().Be("policies");
        }

        both[Reads[0]].Should().Contain("cache-store-value").And.NotContain("rate-limit");
        both[ProofFile].Should().NotContain("rate-limit").And.NotContain("cache-");
    }

    [Fact]
    public void NoReportBackToDriverApp_ReplacesOnlyTheCallsToTheDriverApp()
    {
        var healthy = DriverReportsWorkflow(set: false);
        var toggled = DriverReportsWorkflow(set: true);

        Calls(healthy, "/v1/report-outcomes").Should().Be(4);
        Calls(toggled, "/v1/report-outcomes").Should().Be(0, "no call is made to the Driver app");
        Calls(toggled, "/v1/").Should().Be(Calls(healthy, "/v1/") - 4, "the call to the TMS stays");
        toggled.Should().Contain("Report_recorded").And.Contain("Report_not_recorded", "the steps stay, so the rest of the workflow runs as it does");
        toggled.Should().Contain("trackedProperties");
        JsonNode.Parse(toggled)!["definition"]!["actions"].Should().NotBeNull();
    }

    [Fact]
    public void Presets_FixedSetsNoneOfThem_AsFoundASomeOfThem()
    {
        string[] toggles = ["staleThresholdsUnset", "proofMissingThresholdUnset", "lateDeliveryCheckOff", "shipperTrackingCached", "noShipperRateLimit", "noReportBackToDriverApp"];
        var fixedPreset = Preset("fixed");
        var asFound = Preset("as-found");

        toggles.Should().OnlyContain(toggle => !fixedPreset[toggle]!.GetValue<bool>());
        var set = toggles.Select(toggle => asFound[toggle]!.GetValue<bool>()).ToList();
        set.Should().Contain(true).And.Contain(false);
    }

    private const string RateLimit = "RateLimit";
    private const string CacheLookup = "CacheLookup";
    private const string CacheStore = "CacheStore";

    // The fragment's text in infra/estate/partner-api, taken out of a policy.
    private static string Remove(string policy, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            var text = File.ReadAllText(Path.Combine(Root, "infra", "estate", "partner-api", fragment switch
            {
                RateLimit => "shipper-rate-limit.fragment.xml",
                CacheLookup => "shipper-cache-lookup.fragment.xml",
                _ => "shipper-cache-store.fragment.xml",
            }));
            policy = policy.Replace(text, string.Empty, StringComparison.Ordinal);
        }
        return policy;
    }

    private static Dictionary<string, string> Policies(params string[] toggles)
    {
        var resources = BicepSnapshot.Resources(PartnerApi,
            ["param location = 'westeurope'", "param tags = {}", "param apiManagementIdentityName = 'id-apim'", "param partnerApiClientId = 'partner'",
                "param shippersByClientId = 'c=SHIP'", "param personaClientIds = []", "param carriersByClientId = 'c=CAR'",
                "param intakeLogicAppHostName = 'intake'", "param intakeTriggerAudience = 'a'", "param answerLogicAppHostName = 'answer'",
                "param answerTriggerAudience = 'a'", "param carrierInvoiceLogicAppHostName = 'invoice'", "param carrierInvoiceTriggerAudience = 'a'",
                "param tmsApiClientId = 'tms'", "param tmsBaseUrl = 'https://tms'", "param serviceBusName = 'sb'", "param trackingQueueName = 'q'",
                "param proofOfDeliveryQueueName = 'q'", "param inboundStorageAccountName = 'st'", "param inboundContainerName = 'c'",
                "param applicationInsightsName = 'appi'",
                "param partnerPortalPrincipalId = '00000000-0000-0000-0000-000000000001'", "param simulatorPrincipalId = '00000000-0000-0000-0000-000000000002'",
                "param bookingPrincipalId = '00000000-0000-0000-0000-000000000003'", "param shipperNotificationPrincipalId = '00000000-0000-0000-0000-000000000004'",
                "param shipperInvoicePrincipalId = '00000000-0000-0000-0000-000000000005'", "param carrierInvoiceOutcomePrincipalId = '00000000-0000-0000-0000-000000000006'",
                "param carrierCancellationPrincipalId = '00000000-0000-0000-0000-000000000007'", "param carrierEndpointKeySeed = 'seed'", .. toggles]);

        return resources
            .Where(resource => resource["type"]!.GetValue<string>() == "Microsoft.ApiManagement/service/apis/operations/policies")
            .Select(resource => (Name: resource["name"]!.GetValue<string>(), Policy: resource["properties"]!["value"]!.GetValue<string>()))
            .Where(policy => policy.Policy.Contains("shipper-tracking", StringComparison.Ordinal))
            .ToDictionary(policy => policy.Name.Split('/').Reverse().Skip(1).First(), policy => policy.Policy);
    }

    private static int Calls(string workflow, string uriPart) =>
        JsonNode.Parse(workflow)!.ToJsonString().Split("\"uri\":\"").Skip(1).Count(uri => uri.Contains(uriPart, StringComparison.Ordinal) && uri.IndexOf('"') > uri.IndexOf(uriPart, StringComparison.Ordinal));

    // What infra/stage-logic-app.sh puts in the zip for the Driver reports workflow, given the toggle.
    private static string DriverReportsWorkflow(bool set)
    {
        var directory = Directory.CreateTempSubdirectory("stage-logic-app-");
        try
        {
            var zip = Path.Combine(directory.FullName, "app.zip");
            var start = new System.Diagnostics.ProcessStartInfo("sh",
                [Path.Combine(Root, "infra", "stage-logic-app.sh"), Path.Combine(Root, "src", "Integration", "DriverReportsLogicApp"), zip, "host.json", "driver-reports"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["ESTATE_ASFOUND"] = $$"""{"noReportBackToDriverApp":{{(set ? "true" : "false")}}}""";
            using var process = System.Diagnostics.Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.Should().Be(0, error);

            using var archive = ZipFile.OpenRead(zip);
            using var reader = new StreamReader(archive.GetEntry("driver-reports/workflow.json")!.Open());
            return reader.ReadToEnd();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static JsonObject Preset(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "infra", "presets", $"{name}.json")))!.AsObject();
}
