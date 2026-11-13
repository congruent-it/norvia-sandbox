using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

/// <summary>
/// The Azure adapter maps what Resource Graph and Service Bus answer to the snapshots' shape. Fakes stand in for Azure; what a real
/// estate answers is the smoke run's to show (infra/smoke-test-estate-health-check.ps1).
/// </summary>
public sealed class AzureEstateConfigurationTests : IDisposable
{
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";
    private const string Namespace = "sb-norvia-abc";
    private const string NamespaceId = $"{ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/{Namespace}";
    private const string Address = $"https://management.azure.com{NamespaceId}";

    private readonly Azure _azure = new();
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();
    private readonly IHttpClientFactory _clients = Substitute.For<IHttpClientFactory>();
    private readonly HttpClient _http;
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 13, 0, 0, 5, TimeSpan.Zero));
    private readonly ICostReader _costReader = Substitute.For<ICostReader>();

    public AzureEstateConfigurationTests()
    {
        _http = new HttpClient(_azure);
        _clients.CreateClient(AzureEstateConfiguration.ManagementClient).Returns(_http);
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("app-token", DateTimeOffset.MaxValue));
    }

    public void Dispose() => _http.Dispose();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ReadAsync_ForTheCost_ReadsOneDayMoreThanTwoPeriods_SoTwoPeriodsOfSettledDaysRemainWithoutTheProvisionalOne()
    {
        _azure.Entities(queues: [], subscriptions: []).Alerts();
        var yesterday = new DateOnly(2026, 11, 12);
        _costReader.ReadAsync(new DateOnly(2026, 10, 29), yesterday, Arg.Any<CancellationToken>()).Returns(
        [
            new CostDay(yesterday.AddDays(-1), 40, false, [new("log-workspace", 0.5m)]),
            new CostDay(yesterday, 40, true, [new("log-workspace", 9m)]),
        ]);

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Cost.Should().NotBeNull();
        estate.Cost!.Days.Select(day => day.Date).Should().Equal(yesterday.AddDays(-1));
    }

    [Fact]
    public async Task ReadAsync_WhenTheCostCannotBeRead_LeavesItNull_SoTheCostCheckGivesNoFinding()
    {
        _azure.Entities(queues: [], subscriptions: []).Alerts();
        _costReader.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CostDay>>>(_ => throw new HttpRequestException("403"));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Cost.Should().BeNull();
        Checklist.Default.Run(estate).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ForQueuesAndSubscriptions_SaysWhichHaveAnAlertOnTheirDeadLetterQueue()
    {
        _azure.Entities(queues: ["carrier-tracking", "carrier-proof-of-delivery"], subscriptions: [("shipment-events", "carrier-booking")])
            .Alerts(Alert("alert-dead-letter-carrier-tracking", entities: ["carrier-tracking"]), Alert("alert-dead-letter-carrier-booking", entities: ["carrier-booking"]));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Name.Should().Be("azure");
        estate.ServiceBusEntities.Select(entity => (entity.Name, entity.HasDeadLetterAlert)).Should().Equal(
            ("carrier-tracking", true), ("carrier-proof-of-delivery", false), ("carrier-booking", true));
    }

    [Fact]
    public async Task ReadAsync_WhenAnAlertIsMissing_GivesTheFindingTheSnapshotsDo()
    {
        _azure.Entities(queues: ["carrier-tracking", "carrier-proof-of-delivery"], subscriptions: [])
            .Alerts(Alert("alert-dead-letter-carrier-proof-of-delivery", entities: ["carrier-proof-of-delivery"]));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        var finding = Checklist.Default.Run(estate).Should().ContainSingle().Subject;
        finding.CheckId.Should().Be("DLQ-ALERT");
        finding.Evidence.Select(evidence => evidence.Subject).Should().Equal("carrier-tracking");
    }

    [Fact]
    public async Task ReadAsync_WhenEveryAlertIsThere_GivesNoFinding()
    {
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: [("shipment-events", "carrier-booking")])
            .Alerts(Alert("a", entities: ["carrier-tracking"]), Alert("b", entities: ["carrier-booking"]));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        Checklist.Default.Run(estate).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task ReadAsync_ForAnAlertWhoseEnabledFlagResourceGraphAnswersAsANumber_ReadsIt(int enabled, bool watching)
    {
        var alert = Alert("a", entities: ["carrier-tracking"]);
        alert["enabled"] = enabled;
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: []).Alerts(alert);

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle().Which.HasDeadLetterAlert.Should().Be(watching);
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertThatIsOff_DoesNotCountItAsWatching()
    {
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: []).Alerts(Alert("a", entities: ["carrier-tracking"], enabled: false));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle().Which.HasDeadLetterAlert.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertOnAnotherResource_DoesNotCountItAsWatching()
    {
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: [])
            .Alerts(Alert("a", entities: ["carrier-tracking"], scope: $"{ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/another"));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle().Which.HasDeadLetterAlert.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertOnAnotherMetric_DoesNotCountItAsWatching()
    {
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: []).Alerts(Alert("a", entities: ["carrier-tracking"], metric: "ActiveMessages"));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle().Which.HasDeadLetterAlert.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertWithNoEntityDimension_CountsItAsWatchingEveryEntity()
    {
        _azure.Entities(queues: ["carrier-tracking", "carrier-booking"], subscriptions: []).Alerts(Alert("a", entities: null));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().OnlyContain(entity => entity.HasDeadLetterAlert);
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertThatExcludesAnEntity_DoesNotCountItAsWatchingIt()
    {
        _azure.Entities(queues: ["carrier-tracking"], subscriptions: []).Alerts(Alert("a", entities: ["carrier-tracking"], dimensionOperator: "Exclude"));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle().Which.HasDeadLetterAlert.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_ForAnAlertWithAWildcard_CountsItAsWatchingEveryEntity()
    {
        _azure.Entities(queues: ["carrier-tracking", "carrier-booking"], subscriptions: []).Alerts(Alert("a", entities: ["*"]));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().OnlyContain(entity => entity.HasDeadLetterAlert);
    }

    [Fact]
    public async Task ReadAsync_LeavesOutTheSubscriptionsTheEstateExempts_AsAlertsBicepDoes()
    {
        _azure.Entities(queues: [], subscriptions: [("transport-order-events", "monitoring"), ("trip-events", "monitoring"), ("shipment-events", "carrier-booking")])
            .Alerts();

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Select(entity => entity.Name).Should().Equal("carrier-booking");
    }

    [Fact]
    public async Task ReadAsync_ForAnEntityNameThatTwoTopicsShare_ListsItOnce()
    {
        _azure.Entities(queues: [], subscriptions: [("a-events", "billing"), ("b-events", "billing")]).Alerts();

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_AcrossPages_FollowsTheSkipTokenAndTheNextLink()
    {
        _azure.Entities(queues: ["first"], subscriptions: [], queuesNextLink: $"{Address}/queues?api-version=2024-01-01&$skiptoken=2", moreQueues: ["second"])
            .Alerts("page-2", [Alert("a", entities: ["first"])], [Alert("b", entities: ["second"])]);

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Select(entity => (entity.Name, entity.HasDeadLetterAlert)).Should().Equal(("first", true), ("second", true));
    }

    [Fact]
    public async Task ReadAsync_AsksResourceGraphOnlyForTheEstatesMetricAlerts_AsTheAppsOwnIdentity()
    {
        _azure.Entities(queues: [], subscriptions: []).Alerts();

        await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        var graph = _azure.Requests.Single(request => request.Uri.Contains("ResourceGraph", StringComparison.Ordinal) && request.Body.Contains("microsoft.insights/metricalerts", StringComparison.Ordinal));
        graph.Method.Should().Be(HttpMethod.Post);
        var body = JsonNode.Parse(graph.Body)!;
        body["subscriptions"]!.AsArray().Select(id => id!.GetValue<string>()).Should().Equal("00000000-0000-0000-0000-00000000000a");
        body["query"]!.GetValue<string>().Should().Contain("resourceGroup =~ 'rg-norvia'");
        _azure.Requests.Should().OnlyContain(request => request.Authorization == "Bearer app-token");
        _azure.Requests.Where(request => request.Method != HttpMethod.Get).Should().OnlyContain(
            request => request.Uri.Contains("ResourceGraph", StringComparison.Ordinal),
            "the only things posted are queries; nothing is written");
        await _credential.Received().GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "https://management.azure.com/.default"), Arg.Any<CancellationToken>());
    }

    private const string AppsBase = $"{ResourceGroupId}/providers";
    private const string InboundId = $"{AppsBase}/Microsoft.Storage/storageAccounts/stinboundabc";
    private const string TransitId = $"{AppsBase}/Microsoft.Storage/storageAccounts/stdriverabc";
    private const string TmsStorageId = $"{AppsBase}/Microsoft.Storage/storageAccounts/storageabc";
    private const string ApiManagementId = $"{AppsBase}/Microsoft.ApiManagement/service/apim-abc";

    private static JsonObject Row(params (string Key, JsonNode? Value)[] cells)
    {
        var row = new JsonObject();
        foreach (var (key, value) in cells)
        {
            row[key] = value;
        }

        return row;
    }

    private static string LifecyclePolicy(string prefix, string action) =>
        new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["policy"] = new JsonObject
                {
                    ["rules"] = new JsonArray(new JsonObject
                    {
                        ["enabled"] = true,
                        ["definition"] = new JsonObject
                        {
                            ["filters"] = new JsonObject { ["prefixMatch"] = new JsonArray(prefix) },
                            ["actions"] = new JsonObject { ["baseBlob"] = new JsonObject { [action] = new JsonObject { ["daysAfterModificationGreaterThan"] = 30 } } },
                        },
                    }),
                },
            },
        }.ToJsonString();

    private async Task<Norvia.HealthCheck.Application.Estate.EstateConfiguration> ReadEmptyEstateAsync()
    {
        _azure.Entities(queues: [], subscriptions: []).Alerts();
        return await Adapter().ReadAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadAsync_ReportsEachStepAsItStarts_InTheOrderItReadsThem()
    {
        var steps = new List<string>();
        _azure.Entities(queues: [], subscriptions: []).Alerts();

        await Adapter().ReadAsync(TestContext.Current.CancellationToken, new Steps(steps));

        steps.Should().Equal(
            "Reading the alert rules",
            "Reading the Service Bus queues and subscriptions",
            "Reading the Logic Apps",
            "Reading the TMS API's settings",
            "Reading API Management's policies",
            "Reading how long Application Insights and its workspace keep what they trace",
            "Reading the storage accounts' lifecycle rules and malware scanning",
            "Counting the failed runs of each Logic App",
            "Reading the spend and the Transport Order volume",
            "Reading the alert receivers");
    }

    // Progress<T> posts to a context of its own; a test wants the steps as they are reported.
    private sealed class Steps(List<string> steps) : IProgress<string>
    {
        public void Report(string value) => steps.Add(value);
    }

    [Fact]
    public async Task ReadAsync_WhatReaderCannotSee_IsLeftUnknown_NotHealthy()
    {
        var estate = await ReadEmptyEstateAsync();

        estate.InlinedCarrierSecrets.Should().BeNull("a Logic App's settings need more than Reader");
        estate.DriverReportsReportBack.Should().BeNull();
        estate.Agents.Should().BeNull();
        estate.AlertReceivers.Should().BeNull();
        estate.Retention.Should().BeNull();
        estate.ConversationSweeperRuns.Should().BeNull();
        estate.Storage.Should().Be(new Norvia.HealthCheck.Application.Estate.StorageFacts(null, null, null, null));
        estate.BusinessSafetyNets.Should().Be(new Norvia.HealthCheck.Application.Estate.BusinessSafetyNetFacts(null, null, null));
        estate.ApiManagement.Should().Be(new Norvia.HealthCheck.Application.Estate.ApiManagementFacts(null, null));
        Checklist.Default.Run(estate).Should().BeEmpty("a Check gives no Finding for what it could not see");
    }

    [Theory]
    [InlineData(0, "ALERT-RECEIVER")]
    [InlineData(1, null)]
    public async Task ReadAsync_CountsTheReceiversOfTheActionGroup(int receivers, string? finding)
    {
        _azure.Resources("microsoft.insights/actiongroups", Row(("enabled", true), ("receivers", receivers)));

        var estate = await ReadEmptyEstateAsync();

        estate.AlertReceivers.Should().Be(receivers);
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().Equal(finding is null ? [] : [finding]);
    }

    [Fact]
    public async Task ReadAsync_SaysWhichLogicAppsHaveAFailedRunAlert_AndWhichStandInForTheSweeper()
    {
        const string tracking = $"{AppsBase}/Microsoft.Web/sites/logic-tracking-abc";
        const string billing = $"{AppsBase}/Microsoft.Web/sites/logic-billing-abc";
        _azure.Resources(
                "microsoft.web/sites",
                Row(("id", tracking), ("name", "logic-tracking-abc"), ("enabled", true), ("workload", "")),
                Row(("id", billing), ("name", "logic-billing-abc"), ("enabled", true), ("workload", "")),
                Row(("id", $"{AppsBase}/Microsoft.Web/sites/logic-sweeper-abc"), ("name", "logic-sweeper-abc"), ("enabled", false), ("workload", "conversation-sweeper")))
            .Entities(queues: [], subscriptions: [])
            .Alerts(Alert("alert-failed-runs-carrier-tracking", entities: null, scope: tracking, metric: "WorkflowRunsCompleted"));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Flows.Select(flow => (flow.Name, flow.HasFailedRunAlert, flow.RecordsTrackedProperties)).Should().Equal(
            ("logic-tracking-abc", true, null), ("logic-billing-abc", false, null), ("logic-sweeper-abc", false, null));
        estate.ConversationSweeperRuns.Should().BeFalse();
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().BeEquivalentTo("FAILED-RUN-ALERT", "CONVERSATION-SWEEPER");
    }

    [Fact]
    public async Task ReadAsync_CountsTheFailedRunsOfEachLogicApp_FromTheWorkflowRunsMetric()
    {
        const string tracking = $"{AppsBase}/Microsoft.Web/sites/logic-tracking-abc";
        const string billing = $"{AppsBase}/Microsoft.Web/sites/logic-billing-abc";
        _azure.Resources(
                "microsoft.web/sites",
                Row(("id", tracking), ("name", "logic-tracking-abc"), ("enabled", true), ("workload", "")),
                Row(("id", billing), ("name", "logic-billing-abc"), ("enabled", true), ("workload", "")))
            .Entities(queues: [], subscriptions: [])
            .Raw($"{tracking}/providers/microsoft.insights/metrics", Metric(6, 8))
            .Raw($"{billing}/providers/microsoft.insights/metrics", Metric());

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Flows.Select(flow => (flow.Name, flow.FailedRuns)).Should().Equal(("logic-tracking-abc", 14), ("logic-billing-abc", 0));
        var request = _azure.Requests.Single(recorded => recorded.Uri.Contains($"{tracking}/providers/microsoft.insights/metrics", StringComparison.Ordinal)).Uri;
        request.Should().Contain("metricnames=WorkflowRunsCompleted").And.Contain("status%20eq%20%27Failed%27");
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().Contain(UntoldFailedRunsCheck.CheckId);
    }

    [Fact]
    public async Task ReadAsync_ReadsTheFailedRunsOfManyLogicAppsTogether_ButNotMoreThanEightAtOnce_AndKeepsTheirOrder()
    {
        var apps = Enumerable.Range(1, 20).Select(number => (Id: $"{AppsBase}/Microsoft.Web/sites/logic-{number:00}-abc", Name: $"logic-{number:00}-abc")).ToList();
        _azure.Resources("microsoft.web/sites", [.. apps.Select(app => Row(("id", app.Id), ("name", app.Name), ("enabled", true), ("workload", "")))])
            .Entities(queues: [], subscriptions: []);
        foreach (var (app, number) in apps.Select((app, index) => (app, index + 1)))
        {
            _azure.Raw($"{app.Id}/providers/microsoft.insights/metrics", Metric(number));
        }

        _azure.Delay = TimeSpan.FromMilliseconds(40);

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Flows.Select(flow => (flow.Name, flow.FailedRuns)).Should().Equal(apps.Select((app, index) => (app.Name, (int?)(index + 1))));
        _azure.MostAtOnce.Should().BeInRange(2, 8);
    }

    [Fact]
    public async Task ReadAsync_StartsItsResourceGraphQueriesTogether_AndStillReportsTheStepsInOrder()
    {
        var steps = new List<string>();
        _azure.Entities(queues: [], subscriptions: []);
        _azure.Delay = TimeSpan.FromMilliseconds(100);

        await Adapter().ReadAsync(TestContext.Current.CancellationToken, new Steps(steps));

        _azure.MostGraphAtOnce.Should().BeGreaterThanOrEqualTo(6, "the eight queries do not wait for one another");
        steps.Take(3).Should().Equal("Reading the alert rules", "Reading the Service Bus queues and subscriptions", "Reading the Logic Apps");
    }

    [Fact]
    public async Task ReadAsync_WhenAQueryFails_FailsTheReadOnceTheOthersHaveEnded()
    {
        _azure.Refuse();

        var read = () => Adapter().ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ReadAsync_ForALogicAppWithNoMetric_LeavesItsFailedRunsUnknown_AndGivesNoUntoldFinding()
    {
        _azure.Resources("microsoft.web/sites", Row(("id", $"{AppsBase}/Microsoft.Web/sites/logic-tracking-abc"), ("name", "logic-tracking-abc"), ("enabled", true), ("workload", "")))
            .Entities(queues: [], subscriptions: []);

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.Flows.Should().ContainSingle().Which.FailedRuns.Should().BeNull();
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().NotContain(UntoldFailedRunsCheck.CheckId);
    }

    [Fact]
    public async Task ReadAsync_ReadsTheDeadLetteredMessagesOfEachQueueAndSubscription()
    {
        _azure.Entities(
                queues: ["carrier-tracking", "billing"],
                subscriptions: [("shipment-events", "carrier-booking")],
                deadLettered: new() { ["carrier-tracking"] = 3, ["carrier-booking"] = 1 })
            .Alerts(Alert("a", entities: ["carrier-booking"]));

        var estate = await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        estate.ServiceBusEntities.Select(entity => (entity.Name, entity.DeadLetteredMessages)).Should().Equal(("carrier-tracking", 3), ("billing", 0), ("carrier-booking", 1));
        var untold = Checklist.Default.Run(estate).Single(finding => finding.CheckId == UntoldDeadLettersCheck.CheckId);
        untold.Evidence.Select(evidence => evidence.Subject).Should().Equal("carrier-tracking");
    }

    [Fact]
    public async Task ReadAsync_ComparesWhatApplicationInsightsKeepsWithTheWorkspace()
    {
        _azure.Resources("microsoft.insights/components", Row(("days", 30))).Resources("microsoft.operationalinsights/workspaces", Row(("days", 90)));

        var estate = await ReadEmptyEstateAsync();

        estate.Retention.Should().Be(new Norvia.HealthCheck.Application.Estate.RetentionFacts(30, 90));
        Checklist.Default.Run(estate).Should().ContainSingle().Which.CheckId.Should().Be("APPINSIGHTS-RETENTION");
    }

    [Fact]
    public async Task ReadAsync_ReadsTheTmsSettings_FromTheContainerAppsEnvironment()
    {
        _azure.Resources(
            "microsoft.app/containerapps",
            Row(("env", new JsonArray(
                new JsonObject { ["name"] = "ShipmentStale__ThresholdsSet", ["value"] = "false" },
                new JsonObject { ["name"] = "ProofsOfDelivery__MissingThresholdSet", ["value"] = "true" },
                new JsonObject { ["name"] = "LateDelivery__CheckEnabled", ["value"] = "false" },
                new JsonObject { ["name"] = "A_SECRET", ["secretRef"] = "x" }))));

        var estate = await ReadEmptyEstateAsync();

        estate.BusinessSafetyNets.Should().Be(new Norvia.HealthCheck.Application.Estate.BusinessSafetyNetFacts(false, true, false));
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().BeEquivalentTo("STALE-THRESHOLDS", "LATE-DELIVERY-CHECK");
    }

    [Fact]
    public async Task ReadAsync_ReadsTheStorageProtections_FromTheLifecycleRulesAndTheDefenderSetting()
    {
        _azure.Resources("microsoft.storage/storageaccounts", Row(("id", InboundId), ("name", "stinboundabc")), Row(("id", TransitId), ("name", "stdriverabc")), Row(("id", TmsStorageId), ("name", "storageabc")))
            .Raw($"{InboundId}/managementPolicies/default", LifecyclePolicy("inbound-proofs-of-delivery/", "delete"))
            .Raw($"{TmsStorageId}/managementPolicies/default", LifecyclePolicy("proofs-of-delivery/", "tierToCool"))
            .Raw($"{InboundId}/providers/Microsoft.Security/defenderForStorageSettings/current", """{"properties":{"isEnabled":true,"malwareScanning":{"onUpload":{"isEnabled":true}}}}""");

        var estate = await ReadEmptyEstateAsync();

        estate.Storage.Should().Be(new Norvia.HealthCheck.Application.Estate.StorageFacts(true, false, true, true, "stinboundabc", "stdriverabc"));
        Checklist.Default.Run(estate).Should().ContainSingle().Which.CheckId.Should().Be("LIFECYCLE-TRANSIT");
    }

    [Fact]
    public async Task ReadAsync_WhenTheStorageHasNoPolicyAndNoDefenderSetting_FindsTheProtectionsAbsent()
    {
        _azure.Resources("microsoft.storage/storageaccounts", Row(("id", InboundId), ("name", "stinboundabc")), Row(("id", TmsStorageId), ("name", "storageabc")));

        var estate = await ReadEmptyEstateAsync();

        estate.Storage.Should().Be(new Norvia.HealthCheck.Application.Estate.StorageFacts(false, null, false, false, "stinboundabc", null));
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().BeEquivalentTo("LIFECYCLE-INBOUND", "PROOF-HOT-TIER", "MALWARE-SCANNING");
        Checklist.Default.Run(estate).Single(item => item.CheckId == "MALWARE-SCANNING").Evidence.Select(evidence => evidence.Subject)
            .Should().Equal("inbound storage account stinboundabc");
    }

    [Theory]
    [InlineData(86400, true, new[] { "APIM-CACHE" })]
    [InlineData(0, false, new[] { "APIM-RATE-LIMIT" })]
    public async Task ReadAsync_ReadsTheCacheAndTheRateLimit_FromTheShipperTrackingPolicies(int cacheSeconds, bool limited, string[] findings)
    {
        _azure.Resources("microsoft.apimanagement/service", Row(("id", ApiManagementId)));
        foreach (var operation in new[] { "list-shipper-transport-orders", "find-shipper-transport-order", "get-shipper-transport-order" })
        {
            var policy = (limited ? "<rate-limit-by-key calls=\"120\" /> " : string.Empty) + (cacheSeconds > 0 ? $"<cache-store-value key=\"k\" value=\"v\" duration=\"{cacheSeconds}\" />" : string.Empty);
            _azure.Raw($"{ApiManagementId}/apis/norvia-partner-api/operations/{operation}/policies/policy", new JsonObject { ["properties"] = new JsonObject { ["value"] = policy } }.ToJsonString());
        }

        var estate = await ReadEmptyEstateAsync();

        estate.ApiManagement.Should().Be(new Norvia.HealthCheck.Application.Estate.ApiManagementFacts(cacheSeconds, limited));
        Checklist.Default.Run(estate).Select(item => item.CheckId).Should().BeEquivalentTo(findings);
    }

    [Fact]
    public async Task ReadAsync_WhenAzureRefuses_Fails()
    {
        _azure.Refuse();

        var read = async () => await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ReadAsync_ForAnEntityWithoutAName_Fails()
    {
        _azure.Raw("queues", """{"value":[{"properties":{}}]}""").Alerts();

        var read = async () => await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadAsync_WhenAzureAnswersNothing_Fails()
    {
        _azure.Raw("queues", "null").Alerts();

        var read = async () => await Adapter().ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("", Namespace)]
    [InlineData("/subscriptions/not-a-guid/resourceGroups/rg", Namespace)]
    [InlineData(ResourceGroupId, "")]
    public void TheOptions_WithoutAnEstate_AreNotValid(string resourceGroupId, string serviceBusNamespace)
    {
        var options = new AzureEstateOptions { ResourceGroupId = resourceGroupId, ServiceBusNamespace = serviceBusNamespace };

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), [], validateAllProperties: true);

        valid.Should().BeFalse();
    }

    private AzureEstateConfiguration Adapter() =>
        new(_clients, _credential, Microsoft.Extensions.Options.Options.Create(new AzureEstateOptions { ResourceGroupId = ResourceGroupId, ServiceBusNamespace = Namespace }), _time, _costReader, NullLogger<AzureEstateConfiguration>.Instance);

    // Azure Monitor's answer for a metric: one time series with a point for each day's total, or none at all.
    private static string Metric(params int[] totals) =>
        new JsonObject
        {
            ["value"] = new JsonArray(new JsonObject
            {
                ["timeseries"] = new JsonArray(new JsonObject { ["data"] = new JsonArray(totals.Select(total => (JsonNode?)new JsonObject { ["total"] = total }).ToArray()) }),
            }),
        }.ToJsonString();

    private static JsonObject Alert(string name, string[]? entities, bool enabled = true, string scope = NamespaceId, string metric = "DeadletteredMessages", string dimensionOperator = "Include") => new()
    {
        ["name"] = name,
        ["enabled"] = enabled,
        ["scopes"] = new JsonArray(scope),
        ["criteria"] = new JsonObject
        {
            ["allOf"] = new JsonArray(new JsonObject
            {
                ["metricName"] = metric,
                ["dimensions"] = entities is null
                    ? new JsonArray()
                    : new JsonArray(new JsonObject
                    {
                        ["name"] = "EntityName",
                        ["operator"] = dimensionOperator,
                        ["values"] = new JsonArray(entities.Select(entity => (JsonNode?)JsonValue.Create(entity)).ToArray()),
                    }),
            }),
        },
    };

    private sealed record Recorded(HttpMethod Method, string Uri, string? Authorization, string Body);

    /// <summary>Resource Graph and the namespace's management plane, answering from what the test sets.</summary>
    private sealed class Azure : HttpMessageHandler
    {
        private readonly List<(string Contains, string Content)> _answers = [];
        private readonly List<JsonObject> _alerts = [];
        private readonly List<(string Type, JsonObject Row)> _resources = [];
        private string? _skipToken;
        private List<JsonObject> _moreAlerts = [];
        private bool _refuse;
        private int _inFlight;
        private int _graphInFlight;

        public List<Recorded> Requests { get; } = [];

        /// <summary>How long each Azure Resource Manager answer takes, so reads that run together can be told from reads that run in turn.</summary>
        public TimeSpan Delay { get; set; }

        /// <summary>The most Azure Resource Manager requests that were waiting for an answer at one time.</summary>
        public int MostAtOnce { get; private set; }

        /// <summary>The most Resource Graph queries that were waiting for an answer at one time.</summary>
        public int MostGraphAtOnce { get; private set; }

        public Azure Entities(string[] queues, (string Topic, string Name)[] subscriptions, string? queuesNextLink = null, string[]? moreQueues = null, Dictionary<string, int>? deadLettered = null)
        {
            string Items(IEnumerable<string> names, string? nextLink = null)
            {
                var page = new JsonObject
                {
                    ["value"] = new JsonArray(names.Select(name => (JsonNode?)new JsonObject
                    {
                        ["name"] = name,
                        ["properties"] = new JsonObject { ["countDetails"] = new JsonObject { ["deadLetterMessageCount"] = deadLettered?.GetValueOrDefault(name) ?? 0 } },
                    }).ToArray()),
                };
                if (nextLink is not null)
                {
                    page["nextLink"] = nextLink;
                }

                return page.ToJsonString();
            }

            Raw(Address + "/queues?", Items(queues, queuesNextLink));
            // The last answer that fits wins, so the second page, which fits the first's address too, is added after it.
            Raw("api-version=2024-01-01&$skiptoken=2", Items(moreQueues ?? []));
            var topics = subscriptions.Select(subscription => subscription.Topic).Distinct().ToList();
            Raw(Address + "/topics?", Items(topics));
            foreach (var topic in topics)
            {
                Raw($"/topics/{topic}/subscriptions?", Items(subscriptions.Where(subscription => subscription.Topic == topic).Select(subscription => subscription.Name)));
            }

            return this;
        }

        public Azure Alerts(params JsonObject[] alerts)
        {
            _alerts.AddRange(alerts);
            return this;
        }

        /// <summary>The alerts in two pages: the first answers with a skip token, and asking again with it gets the second.</summary>
        public Azure Alerts(string skipToken, JsonObject[] first, JsonObject[] second)
        {
            _skipToken = skipToken;
            _alerts.AddRange(first);
            _moreAlerts = [.. second];
            return this;
        }

        /// <summary>The rows Resource Graph answers a query over resources of this type with.</summary>
        public Azure Resources(string type, params JsonObject[] rows)
        {
            _resources.AddRange(rows.Select(row => (type, row)));
            return this;
        }

        public Azure Raw(string contains, string content)
        {
            _answers.Add((contains, content));
            return this;
        }

        public void Refuse() => _refuse = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            }

            if (_refuse)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (request.RequestUri.AbsoluteUri.Contains("ResourceGraph", StringComparison.Ordinal))
            {
                var querying = Interlocked.Increment(ref _graphInFlight);
                lock (Requests)
                {
                    MostGraphAtOnce = Math.Max(MostGraphAtOnce, querying);
                }

                try
                {
                    await Task.Delay(Delay, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _graphInFlight);
                }

                var query = JsonNode.Parse(body)!["query"]!.GetValue<string>();
                if (!query.Contains("microsoft.insights/metricalerts", StringComparison.Ordinal))
                {
                    var rows = _resources.Where(resource => query.Contains($"'{resource.Type}'", StringComparison.Ordinal)).Select(resource => (JsonNode?)resource.Row.DeepClone());
                    return Json(new JsonObject { ["data"] = new JsonArray(rows.ToArray()) }.ToJsonString());
                }

                var next = JsonNode.Parse(body)!["options"]?["$skipToken"] is not null;
                var data = next ? _moreAlerts : _alerts;
                var page = new JsonObject { ["data"] = new JsonArray(data.Select(alert => (JsonNode?)alert.DeepClone()).ToArray()) };
                if (!next && _skipToken is not null)
                {
                    page["$skipToken"] = _skipToken;
                }

                return Json(page.ToJsonString());
            }

            var address = request.RequestUri.AbsoluteUri;
            var waiting = Interlocked.Increment(ref _inFlight);
            lock (Requests)
            {
                MostAtOnce = Math.Max(MostAtOnce, waiting);
            }

            try
            {
                await Task.Delay(Delay, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }

            var answer = _answers.LastOrDefault(candidate => address.Contains(candidate.Contains, StringComparison.Ordinal));
            return answer.Content is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(answer.Content);
        }

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
