using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class ShipperTrackingClientTests : IDisposable
{
    private readonly StubHandler _partnerApi = new();
    private readonly IPersonaTokens _tokens = Substitute.For<IPersonaTokens>();
    private readonly FakeLogger<NorviaPartnerApiClient> _logger = new();
    private readonly IPartnerPortalFaults _faults = Substitute.For<IPartnerPortalFaults>();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly HttpClient _http;
    private readonly NorviaPartnerApiClient _client;

    public ShipperTrackingClientTests()
    {
        _http = new HttpClient(_partnerApi) { BaseAddress = new Uri("https://apim.example/partner/v1/") };
        _tokens.GetTokenAsync(Samples.BrouwerDranken.Id, Arg.Any<CancellationToken>()).Returns("brouwer-token");
        var options = Options.Create(new PartnerApiOptions
        {
            BaseAddress = _http.BaseAddress,
            PersonaClientIds = "brouwer-dranken=client-1",
            SubscriptionKeys = new() { ["brouwer-dranken"] = "brouwer-key" },
        });
        _client = new NorviaPartnerApiClient(_http, _tokens, options, _activities, _faults, _logger);
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _partnerApi.Dispose();
    }

    private static readonly string Delivered = Contracts.Read("PartnerApi/samples/shipper-tracking/delivered-winter.shipper-tracking.json");

    [Fact]
    public async Task ListTransportOrdersAsync_GetsTheListAsThePersona_WithTheFilterInTheQuery()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/shipper-tracking-lists/mixed-statuses.shipper-tracking-list.json"), "corr-1");

        var answer = await _client.ListTransportOrdersAsync(Samples.BrouwerDranken, new ShipperTrackingFilter(ShipperTrackingStatus.Collected), TestContext.Current.CancellationToken);

        _partnerApi.Request!.Method.Should().Be(HttpMethod.Get);
        _partnerApi.Request.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/transport-orders?trackingStatus=Collected&page=1&pageSize=25"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("brouwer-key");
        _partnerApi.Request.Headers.Authorization!.Parameter.Should().Be("brouwer-token");
        answer.IsFound.Should().BeTrue();
        answer.Value.Should().HaveCount(5);
        answer.MessageId.Should().Be("corr-1");
    }

    [Fact]
    public async Task GetTransportOrderAsync_GetsItByNumber()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Delivered, "corr-2");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        _partnerApi.Request!.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/transport-orders/TO-0000008"));
        answer.Value!.TransportOrderNumber.Should().Be("TO-0000008");
    }

    [Fact]
    public async Task FindTransportOrderAsync_FindsItByTheShipperReference_Escaped()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Delivered);

        var answer = await _client.FindTransportOrderAsync(Samples.BrouwerDranken, "BD 2030/8&x", TestContext.Current.CancellationToken);

        _partnerApi.Request!.RequestUri!.AbsoluteUri.Should().Be("https://apim.example/partner/v1/transport-orders/find?shipperReference=BD%202030%2F8%26x");
        answer.IsFound.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadProofOfDeliveryFileAsync_StreamsTheFileWithItsNameAndType()
    {
        var fileId = Guid.Parse("6f1d3b52-8a4e-4c9d-b7e1-2a5c9e0f4d11");
        _partnerApi.AnswerFile("application/pdf", [1, 2, 3], "signed cmr.pdf", "corr-3");

        var answer = await _client.DownloadProofOfDeliveryFileAsync(Samples.BrouwerDranken, "TO-0000008", fileId, TestContext.Current.CancellationToken);

        _partnerApi.Request!.RequestUri.Should().Be(new Uri($"https://apim.example/partner/v1/transport-orders/TO-0000008/proof-of-delivery/files/{fileId}"));
        answer.Value.Should().BeEquivalentTo(new ShipperProofFileContent("signed cmr.pdf", "application/pdf", [1, 2, 3]));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, TrackingOutcome.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, TrackingOutcome.Refused)]
    [InlineData(HttpStatusCode.Unauthorized, TrackingOutcome.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, TrackingOutcome.Refused)]
    [InlineData(HttpStatusCode.BadRequest, TrackingOutcome.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TrackingOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TrackingOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, TrackingOutcome.Unavailable)]
    public async Task GetTransportOrderAsync_ARefusal_IsAnAnswerWithItsOutcome(HttpStatusCode status, TrackingOutcome outcome)
    {
        _partnerApi.Answer(status, "{}", "corr-9");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(outcome);
        answer.Text.Should().NotBeNullOrWhiteSpace();
        answer.MessageId.Should().Be("corr-9");
    }

    [Fact]
    public async Task GetTransportOrderAsync_TrackingUnavailable_NamesTheMessageIdAndWordsOfTheEdge()
    {
        _partnerApi.Answer(HttpStatusCode.ServiceUnavailable, """{ "messageId": "edge-1", "text": "Tracking is temporarily unavailable. Try again shortly." }""");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        (answer.Outcome, answer.MessageId, answer.Text).Should().Be((TrackingOutcome.Unavailable, "edge-1", "Tracking is temporarily unavailable. Try again shortly."));
    }

    [Fact]
    public async Task GetTransportOrderAsync_ATransportOrderNotFound_TakesTheTmsProblemsDetail()
    {
        _partnerApi.Answer(HttpStatusCode.NotFound, """{ "title": "Not found", "detail": "There is no Transport Order TO-0000099." }""");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000099", TestContext.Current.CancellationToken);

        answer.Text.Should().Be("There is no Transport Order TO-0000099.");
    }

    [Fact]
    public async Task GetTransportOrderAsync_ACarrierRefusedByTheEdge_ShowsTheEdgesText()
    {
        _partnerApi.Answer(HttpStatusCode.Forbidden, """{ "messageId": "m-1", "text": "This Persona may not read tracking." }""");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        (answer.Outcome, answer.Text).Should().Be((TrackingOutcome.Refused, "This Persona may not read tracking."));
    }

    [Fact]
    public async Task GetTransportOrderAsync_WhenNorviaIsUnreachable_IsUnavailable_NotAnError()
    {
        _partnerApi.Fail(new HttpRequestException("No route to host."));

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        (answer.Outcome, answer.MessageId).Should().Be((TrackingOutcome.Unavailable, null));
    }

    [Fact]
    public async Task GetTransportOrderAsync_AnAnswerInNoKnownFormat_IsUnavailable()
    {
        _partnerApi.Answer(HttpStatusCode.OK, """{ "norviaOrderNo": "TO-0000008", "status": "LOST", "events": [] }""");

        var answer = await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(TrackingOutcome.Unavailable);
    }

    [Fact]
    public async Task GetTransportOrderAsync_WhenCancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _partnerApi.Fail(new OperationCanceledException());

        var call = () => _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", cancelled.Token);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetTransportOrderAsync_LogsTheTrackingKeys_SoTheTraceJoinsTheMessageTrail()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Delivered, "corr-2");

        await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000008", TestContext.Current.CancellationToken);

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "shipper-tracking",
            ["partyId"] = "SHP-0001",
            ["correlationId"] = "corr-2",
            ["transportOrderNumber"] = "TO-0000008",
            ["outcome"] = "Succeeded",
        });
    }

    [Fact]
    public async Task ListTransportOrdersAsync_Unavailable_LogsItAsFailed_WithAFailureCode()
    {
        _partnerApi.Answer(HttpStatusCode.ServiceUnavailable, "{}", "corr-5");

        await _client.ListTransportOrdersAsync(Samples.BrouwerDranken, new ShipperTrackingFilter(), TestContext.Current.CancellationToken);

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Failed").And.Contain("failureCode", "TrackingUnavailable")
            .And.NotContainKey("transportOrderNumber");
    }

    [Fact]
    public async Task GetTransportOrderAsync_ANotFound_LogsItAsRefused_WithoutAFailureCode()
    {
        _partnerApi.Answer(HttpStatusCode.NotFound, "{}", "corr-6");

        await _client.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000099", TestContext.Current.CancellationToken);

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Refused").And.NotContainKey("failureCode");
    }

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
}
