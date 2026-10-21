using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.DeadLetters;

/// <summary>The console finds the estate's queues and subscriptions and their counts through Azure Resource Manager, as its own identity (ADR 0017).</summary>
public sealed class ArmServiceBusEntitiesTests : IDisposable
{
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";
    private const string Namespace = "sb-norvia-abc";
    private const string Address = $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/{Namespace}";

    private readonly Arm _arm = new();
    private readonly TokenCredential _credential = Substitute.For<TokenCredential>();
    private readonly IHttpClientFactory _clients = Substitute.For<IHttpClientFactory>();
    private readonly HttpClient _http;

    public ArmServiceBusEntitiesTests()
    {
        _http = new HttpClient(_arm);
        _clients.CreateClient(ArmServiceBusEntities.ManagementClient).Returns(_http);
        _credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("console-token", DateTimeOffset.MaxValue));
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task ListAsync_ForAQueueAndATopicWithSubscriptions_ListsEachWithItsDeadLetterCount()
    {
        _arm.Answer("queues", """{"value":[{"name":"carrier-tracking","properties":{"countDetails":{"deadLetterMessageCount":4}}},{"name":"carrier-proof-of-delivery","properties":{"countDetails":{"deadLetterMessageCount":0}}}]}""")
            .Answer("topics", """{"value":[{"name":"shipment-events"}]}""")
            .Answer("topics/shipment-events/subscriptions", """{"value":[{"name":"carrier-booking","properties":{"countDetails":{"deadLetterMessageCount":2}}},{"name":"monitoring","properties":{}}]}""");

        var entities = await Entities().ListAsync(TestContext.Current.CancellationToken);

        entities.Should().Equal(
            new ServiceBusEntity(Namespace, "carrier-tracking", null, 4),
            new ServiceBusEntity(Namespace, "carrier-proof-of-delivery", null, 0),
            new ServiceBusEntity(Namespace, "shipment-events", "carrier-booking", 2),
            new ServiceBusEntity(Namespace, "shipment-events", "monitoring", 0));
    }

    [Fact]
    public async Task ListAsync_AcrossPages_FollowsTheNextLink()
    {
        _arm.Answer("queues", $$"""{"value":[{"name":"first"}],"nextLink":"{{Address}}/queues?api-version=2024-01-01&$skiptoken=2"}""")
            .Answer("queues", "$skiptoken=2", """{"value":[{"name":"second"}]}""")
            .Answer("topics", """{"value":[]}""");

        var entities = await Entities().ListAsync(TestContext.Current.CancellationToken);

        entities.Select(entity => entity.Name).Should().Equal("first", "second");
    }

    [Fact]
    public async Task ListAsync_Always_CallsAsTheConsolesCredential()
    {
        _arm.Answer("queues", """{"value":[]}""").Answer("topics", """{"value":[]}""");

        await Entities().ListAsync(TestContext.Current.CancellationToken);

        _arm.Requests.Should().OnlyContain(request => request.Headers.Authorization!.Parameter == "console-token");
        await _credential.Received().GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "https://management.azure.com/.default"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_WhenAzureRefuses_Fails()
    {
        _arm.Refuse();

        var list = async () => await Entities().ListAsync(TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ListAsync_ForAnEntityWithoutAName_Fails()
    {
        _arm.Answer("queues", """{"value":[{"properties":{}}]}""");

        var list = async () => await Entities().ListAsync(TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(ResourceGroupId, null)]
    [InlineData(null, Namespace)]
    public async Task ListAsync_WithoutAnEstate_SaysTheConsoleIsNotConnected(string? resourceGroupId, string? serviceBusNamespace)
    {
        var entities = Entities(resourceGroupId, serviceBusNamespace);

        var list = async () => await entities.ListAsync(TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<InvalidOperationException>().WithMessage("*isn't connected to an estate*");
    }

    private ArmServiceBusEntities Entities(string? resourceGroupId = ResourceGroupId, string? serviceBusNamespace = Namespace) =>
        new(_clients, _credential, Options.Create(new EstateServiceBusOptions { ResourceGroupId = resourceGroupId, ServiceBusNamespace = serviceBusNamespace }));

    private sealed class Arm : HttpMessageHandler
    {
        private readonly List<(string Address, string Content)> _answers = [];
        private bool _refuse;

        public List<HttpRequestMessage> Requests { get; } = [];

        public Arm Answer(string collection, string content) => Answer(collection, null, content);

        public Arm Answer(string collection, string? contains, string content)
        {
            _answers.Add(($"{Address}/{collection}?api-version=2024-01-01{(contains is null ? string.Empty : "&" + contains)}", content));
            return this;
        }

        public void Refuse() => _refuse = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var address = request.RequestUri!.AbsoluteUri;
            var answer = _answers.LastOrDefault(candidate => address == new Uri(candidate.Address).AbsoluteUri);
            return Task.FromResult(_refuse || answer.Address is null
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.Content, Encoding.UTF8, "application/json") });
        }
    }
}
