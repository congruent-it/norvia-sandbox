using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

/// <summary>
/// The Azure adapter maps what Resource Graph and Service Bus answer to the snapshots' shape. Fakes stand in for Azure; what a real
/// estate answers is the smoke run's to show (infra/smoke-test-estate-health-check.sh).
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

    public AzureEstateConfigurationTests()
    {
        _http = new HttpClient(_azure);
        _clients.CreateClient(AzureEstateConfiguration.ManagementClient).Returns(_http);
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("app-token", DateTimeOffset.MaxValue));
    }

    public void Dispose() => _http.Dispose();

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

        var graph = _azure.Requests.Single(request => request.Uri.Contains("ResourceGraph", StringComparison.Ordinal));
        graph.Method.Should().Be(HttpMethod.Post);
        var body = JsonNode.Parse(graph.Body)!;
        body["subscriptions"]!.AsArray().Select(id => id!.GetValue<string>()).Should().Equal("00000000-0000-0000-0000-00000000000a");
        body["query"]!.GetValue<string>().Should().Contain("microsoft.insights/metricalerts").And.Contain("resourceGroup =~ 'rg-norvia'");
        _azure.Requests.Should().OnlyContain(request => request.Authorization == "Bearer app-token");
        _azure.Requests.Where(request => request.Method != HttpMethod.Get).Should().ContainSingle("the only thing posted is the query; nothing is written");
        await _credential.Received().GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "https://management.azure.com/.default"), Arg.Any<CancellationToken>());
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
        new(_clients, _credential, Microsoft.Extensions.Options.Options.Create(new AzureEstateOptions { ResourceGroupId = ResourceGroupId, ServiceBusNamespace = Namespace }));

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
        private string? _skipToken;
        private List<JsonObject> _moreAlerts = [];
        private bool _refuse;

        public List<Recorded> Requests { get; } = [];

        public Azure Entities(string[] queues, (string Topic, string Name)[] subscriptions, string? queuesNextLink = null, string[]? moreQueues = null)
        {
            static string Items(IEnumerable<string> names, string? nextLink = null)
            {
                var page = new JsonObject { ["value"] = new JsonArray(names.Select(name => (JsonNode?)new JsonObject { ["name"] = name }).ToArray()) };
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

        public Azure Raw(string contains, string content)
        {
            _answers.Add((contains, content));
            return this;
        }

        public void Refuse() => _refuse = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            if (_refuse)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (request.RequestUri.AbsoluteUri.Contains("ResourceGraph", StringComparison.Ordinal))
            {
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
            var answer = _answers.LastOrDefault(candidate => address.Contains(candidate.Contains, StringComparison.Ordinal));
            return answer.Content is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(answer.Content);
        }

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
