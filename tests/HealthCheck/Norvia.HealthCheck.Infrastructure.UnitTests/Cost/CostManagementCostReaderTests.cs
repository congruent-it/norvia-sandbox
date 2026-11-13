using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Infrastructure.Cost;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Cost;

/// <summary>The Azure adapter maps what Cost Management answers to the port's shape. A fake stands in for Azure; real cost is the smoke run's to show.</summary>
public sealed class CostManagementCostReaderTests : IDisposable
{
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";
    private const string Workspace = $"{ResourceGroupId}/providers/Microsoft.OperationalInsights/workspaces/log-norvia-abc";
    private const string Bus = $"{ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/sb-norvia-abc";

    private readonly Cost _azure = new();
    private readonly HttpClient _http;
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();
    private readonly IHttpClientFactory _clients = Substitute.For<IHttpClientFactory>();
    private readonly ITransportOrderVolume _volume = Substitute.For<ITransportOrderVolume>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));

    public CostManagementCostReaderTests()
    {
        _http = new HttpClient(_azure);
        _clients.CreateClient(AzureEstateConfiguration.ManagementClient).Returns(_http);
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>()).Returns(new AccessToken("app-token", DateTimeOffset.MaxValue));
        _volume.CountPerDayAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<DateOnly, int>());
    }

    public void Dispose() => _http.Dispose();

    private CostManagementCostReader Reader() => new(_credential, _clients, Options.Create(new AzureEstateOptions { ResourceGroupId = ResourceGroupId, ServiceBusNamespace = "sb" }), _volume, _time);

    [Fact]
    public async Task ReadAsync_AsksCostManagementForTheResourceGroupsDailyActualCostPerResource_AsTheApp()
    {
        _azure.Rows(Row(0.53, 20261110, Workspace));

        await Reader().ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), TestContext.Current.CancellationToken);

        _azure.Request!.Method.Should().Be(HttpMethod.Post);
        _azure.Request.RequestUri!.AbsoluteUri.Should().StartWith($"https://management.azure.com{ResourceGroupId}/providers/Microsoft.CostManagement/query?api-version=");
        _azure.Request.Headers.Authorization!.ToString().Should().Be("Bearer app-token");
        var body = JsonNode.Parse(_azure.Body!)!;
        body["type"]!.GetValue<string>().Should().Be("ActualCost");
        body["timePeriod"]!["from"]!.GetValue<string>().Should().StartWith("2026-11-10");
        body["timePeriod"]!["to"]!.GetValue<string>().Should().StartWith("2026-11-10");
        body["dataset"]!["granularity"]!.GetValue<string>().Should().Be("Daily");
        body["dataset"]!["grouping"]![0]!["name"]!.GetValue<string>().Should().Be("ResourceId");
    }

    [Fact]
    public async Task ReadAsync_GivesSpendPerResourceNameInEur_NextToTheDaysTransportOrders()
    {
        _azure.Rows(Row(0.53, 20261110, Workspace), Row(0.34, 20261110, Bus), Row(0.60, 20261111, Workspace));
        _volume.CountPerDayAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 10)] = 38 });

        var days = await Reader().ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), TestContext.Current.CancellationToken);

        days.Select(day => (day.Date, day.TransportOrders)).Should().Equal((new DateOnly(2026, 11, 10), 38), (new DateOnly(2026, 11, 11), 0));
        days[0].Spend.Select(spend => (spend.Resource, spend.Eur)).Should().BeEquivalentTo([("log-norvia-abc", 0.53m), ("sb-norvia-abc", 0.34m)]);
        days[1].Spend.Should().ContainSingle().Which.Should().Be(new ResourceSpend("log-norvia-abc", 0.60m));
    }

    [Fact]
    public async Task ReadAsync_AnswersWholePastDaysOnly_AndMarksYesterdayProvisional()
    {
        _azure.Rows(Row(1, 20261111, Workspace), Row(1, 20261112, Workspace), Row(1, 20261113, Workspace));

        var days = await Reader().ReadAsync(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 13), TestContext.Current.CancellationToken);

        days.Select(day => (day.Date, day.Provisional)).Should().Equal((new DateOnly(2026, 11, 11), false), (new DateOnly(2026, 11, 12), true));
    }

    [Fact]
    public async Task ReadAsync_WhenTheBillIsNotInEur_RefusesToCallItEur()
    {
        _azure.Rows(Row(0.53, 20261110, Workspace, "USD"));

        var read = () => Reader().ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*USD*");
    }

    [Fact]
    public async Task ReadAsync_FollowsCostManagementsNextLink_SoNoPageOfSpendIsLeftOut()
    {
        _azure.Pages([Row(0.53, 20261110, Workspace)], [Row(0.34, 20261110, Bus)]);

        var days = await Reader().ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), TestContext.Current.CancellationToken);

        days.Single().Spend.Select(spend => spend.Resource).Should().BeEquivalentTo("log-norvia-abc", "sb-norvia-abc");
    }

    [Fact]
    public async Task ReadAsync_ForAChargeWithNoResource_CallsItUnassigned()
    {
        _azure.Rows(Row(0.10, 20261110, string.Empty));

        var days = await Reader().ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), TestContext.Current.CancellationToken);

        days.Single().Spend.Should().ContainSingle().Which.Should().Be(new ResourceSpend("unassigned", 0.10m));
    }

    private static JsonArray Row(double cost, int usageDate, string resourceId, string currency = "EUR") => [cost, usageDate, resourceId, currency];

    private sealed class Cost : HttpMessageHandler
    {
        private readonly Queue<JsonArray> _pages = new();

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public void Rows(params JsonArray[] rows) => Pages([rows]);

        public void Pages(params JsonArray[][] pages)
        {
            _pages.Clear();
            foreach (var page in pages)
            {
                _pages.Enqueue([.. page]);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var answer = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["columns"] = new JsonArray(Column("Cost"), Column("UsageDate"), Column("ResourceId"), Column("Currency")),
                    ["rows"] = (_pages.Count > 0 ? _pages.Dequeue() : []).DeepClone(),
                },
            };
            if (_pages.Count > 0)
            {
                answer["properties"]!["nextLink"] = request.RequestUri + "&page=next";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
        }

        private static JsonObject Column(string name) => new() { ["name"] = name };
    }
}
