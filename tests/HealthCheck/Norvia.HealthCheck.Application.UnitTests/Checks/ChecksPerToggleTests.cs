using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

/// <summary>
/// Every Check fires for its as-found toggle and only its toggle: the healthy estate gives no Finding, and an estate with one toggle
/// on gives that Check's Finding alone (#393).
/// </summary>
public sealed class ChecksPerToggleTests
{
    private static readonly AgentIdentity Reader = new("estate-health-check", "HealthCheck.Agent.Read");

    private static EstateConfiguration Healthy { get; } = new("healthy", [new("carrier-tracking", true)])
    {
        Flows = [new("carrier-tracking", true, true), new("billing", true, true)],
        Agents = new AgentFacts(Identities: [Reader]),
    };

    // The estate with one toggle on, the Check that must find it, and the resource its evidence must name.
    public static TheoryData<string, EstateConfiguration, string, string> Toggles => new()
    {
        { "entitiesWithoutDeadLetterAlert", Healthy with { ServiceBusEntities = [new("carrier-tracking", false)] }, "DLQ-ALERT", "carrier-tracking" },
        { "flowsWithoutFailedRunAlert", Healthy with { Flows = [new("carrier-tracking", false), new("billing", true)] }, "FAILED-RUN-ALERT", "carrier-tracking" },
        { "noAlertReceiver", Healthy with { AlertReceivers = 0 }, "ALERT-RECEIVER", "action group" },
        { "carrierSecretInlined", Healthy with { InlinedCarrierSecrets = ["booking Logic App setting CARRIER_ENDPOINT_KEY"] }, "SECRET-INLINED", "booking Logic App setting CARRIER_ENDPOINT_KEY" },
        { "appInsightsRetentionShort", Healthy with { Retention = new RetentionFacts(30, 90) }, "APPINSIGHTS-RETENTION", "Application Insights" },
        { "logicAppsWithoutTrackedProperties", Healthy with { Flows = [new("billing", true, false)] }, "TRACKED-PROPERTIES", "billing" },
        { "noInboundLifecycleRule", Healthy with { Storage = new StorageFacts(InboundHasLifecycleRule: false) }, "LIFECYCLE-INBOUND", "inbound Proof of delivery container" },
        { "noTransitLifecycleRule", Healthy with { Storage = new StorageFacts(TransitHasLifecycleRule: false) }, "LIFECYCLE-TRANSIT", "Driver app transit container" },
        { "proofFilesStayHot", Healthy with { Storage = new StorageFacts(ProofFilesMoveToCool: false) }, "PROOF-HOT-TIER", "TMS proofs-of-delivery container" },
        { "noMalwareScanning", Healthy with { Storage = new StorageFacts(InboundMalwareScanning: false) }, "MALWARE-SCANNING", "inbound storage account" },
        { "staleThresholdsUnset", Healthy with { BusinessSafetyNets = new BusinessSafetyNetFacts(StaleThresholdsSet: false) }, "STALE-THRESHOLDS", "TMS" },
        { "proofMissingThresholdUnset", Healthy with { BusinessSafetyNets = new BusinessSafetyNetFacts(ProofMissingThresholdSet: false) }, "PROOF-MISSING-THRESHOLD", "TMS" },
        { "lateDeliveryCheckOff", Healthy with { BusinessSafetyNets = new BusinessSafetyNetFacts(LateDeliveryCheckOn: false) }, "LATE-DELIVERY-CHECK", "TMS" },
        { "shipperTrackingCached", Healthy with { ApiManagement = new ApiManagementFacts(ShipperTrackingCacheSeconds: 86400) }, "APIM-CACHE", "Shipper tracking API" },
        { "noShipperRateLimit", Healthy with { ApiManagement = new ApiManagementFacts(ShipperRateLimit: false) }, "APIM-RATE-LIMIT", "Shipper tracking API" },
        { "noReportBackToDriverApp", Healthy with { DriverReportsReportBack = false }, "DRIVER-REPORT-BACK", "driver-reports" },
        { "noConversationSweeper", Healthy with { ConversationSweeperRuns = false }, "CONVERSATION-SWEEPER", "conversation-sweeper" },
        { "noContentFilterOrPromptShield", Healthy with { Agents = new AgentFacts(ContentFilterAndPromptShield: false, Identities: [Reader]) }, "AGENT-CONTENT-FILTER", "model deployment" },
        { "agentRoleWiderThanRead", Healthy with { Agents = new AgentFacts(Identities: [Reader with { Role = "Contributor", RoleIsRead = false }]) }, "AGENT-ROLE", "estate-health-check" },
        { "noAgentCapOrRateLimit", Healthy with { Agents = new AgentFacts(Identities: [Reader with { HasRateLimit = false }]) }, "AGENT-CAP", "estate-health-check" },
        { "noAgentCostTag", Healthy with { Agents = new AgentFacts(Identities: [Reader with { HasCostTag = false }]) }, "AGENT-COST-TAG", "estate-health-check" },
        { "agentsEnabledInPublicDemoUi", Healthy with { Agents = new AgentFacts(Identities: [Reader], EnabledInPublicDemoUi: true) }, "AGENTS-PUBLIC", "Demo UI" },
        { "unlimitedModelQuota", Healthy with { Agents = new AgentFacts(Identities: [Reader], ModelQuotaUnlimited: true) }, "MODEL-QUOTA", "model deployment" },
    };

    [Fact]
    public void TheHealthyEstate_GivesNoFinding() =>
        Checklist.Default.Run(Healthy).Should().BeEmpty();

    [Theory]
    [MemberData(nameof(Toggles))]
    public void ATogglesCheck_FiresForItsToggleAndOnlyItsToggle(string toggle, EstateConfiguration estate, string checkId, string subject)
    {
        var findings = Checklist.Default.Run(estate);

        var finding = findings.Should().ContainSingle().Subject;
        finding.CheckId.Should().Be(checkId);
        finding.AsFoundToggle.Should().Be(toggle);
        finding.Fix.Should().Contain(toggle, because: "the fix is to put the toggle back");
        finding.Evidence.Select(evidence => evidence.Subject).Should().Contain(subject);
    }

    [Fact]
    public void EveryCheck_HasItsOwnIdAndItsOwnToggle_AndTheTableCoversThemAll()
    {
        var checks = Checklist.Default.Checks;

        checks.Select(check => check.Id).Should().OnlyHaveUniqueItems();
        checks.Select(check => check.AsFoundToggle).Should().OnlyHaveUniqueItems("a Check maps to exactly one toggle");
        checks.Should().OnlyContain(check => !string.IsNullOrWhiteSpace(check.Rule) && !string.IsNullOrWhiteSpace(check.Title) && !string.IsNullOrWhiteSpace(check.Fix));
        Toggles.Select(row => row.Data.Item1).Should().BeEquivalentTo(checks.Select(check => check.AsFoundToggle));
    }

    [Fact]
    public void AFactTheAdapterCouldNotRead_GivesNoFinding()
    {
        var unknown = Healthy with
        {
            AlertReceivers = null,
            Storage = new StorageFacts(null, null, null, null),
            InlinedCarrierSecrets = null,
            Retention = null,
            BusinessSafetyNets = new BusinessSafetyNetFacts(null, null, null),
            ApiManagement = new ApiManagementFacts(null, null),
            DriverReportsReportBack = null,
            ConversationSweeperRuns = null,
            Flows = [new("billing", true, null)],
            Agents = null,
        };

        Checklist.Default.Run(unknown).Should().BeEmpty();
    }

    [Theory]
    [InlineData(300, false)]
    [InlineData(301, true)]
    [InlineData(0, false)]
    public void TheCacheCheck_FiresOnlyForALongLifetime(int seconds, bool fires) =>
        new ShipperCacheCheck().Examine(Healthy with { ApiManagement = new ApiManagementFacts(ShipperTrackingCacheSeconds: seconds) }).Any().Should().Be(fires);

    [Theory]
    [InlineData(false, false, "no cap on tool calls per question and no request-rate limit")]
    [InlineData(false, true, "no cap on tool calls per question.")]
    [InlineData(true, false, "no request-rate limit")]
    public void TheCapCheck_SaysWhichGuardIsMissing(bool callCap, bool rateLimit, string expected) =>
        new AgentCapCheck().Examine(Healthy with { Agents = new AgentFacts(Identities: [Reader with { HasCallCap = callCap, HasRateLimit = rateLimit }]) })
            .Should().ContainSingle().Which.Detail.Should().Contain(expected);

    [Fact]
    public void TheSecretCheck_NamesWhereTheSecretIs_AndTheMaskerLeavesItBe()
    {
        var finding = Checklist.Default.Run(Healthy with { InlinedCarrierSecrets = ["booking Logic App setting CARRIER_ENDPOINT_KEY"] }).Single();

        finding.Severity.Should().Be(Severity.Critical);
        finding.Evidence.Single().Detail.Should().Contain("CARRIER_ENDPOINT_KEY").And.NotContain("***");
    }

    [Fact]
    public void ASecretValueThatGetsIntoEvidence_IsMasked()
    {
        var finding = Checklist.Default.Run(Healthy with { InlinedCarrierSecrets = ["booking Logic App setting CARRIER_ENDPOINT_KEY=0123456789abcdef"] }).Single();

        finding.Evidence.Single().Subject.Should().NotContain("0123456789abcdef").And.Contain("CARRIER_ENDPOINT_KEY=***");
    }
}
