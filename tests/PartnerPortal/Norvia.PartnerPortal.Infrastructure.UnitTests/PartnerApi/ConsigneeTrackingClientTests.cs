using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

/// <summary>The portal asks Norvia for a Consignee's view with the Consignee product's key and no token, the code in a header (ADR 0052).</summary>
public sealed class ConsigneeTrackingClientTests : IDisposable
{
    private const string Code = "0123456789ABCDEF";

    private const string View = """
        {
          "status": "IN_TRANSIT",
          "pickupCity": "Rotterdam", "pickupCountry": "NL",
          "receiverCity": "Utrecht", "receiverCountry": "NL",
          "deliverFrom": "2026-09-25T16:15:00Z", "deliverUntil": "2026-09-25T20:15:00Z",
          "eta": "2026-09-25T19:00:00Z", "lastUpdate": "2026-09-25T11:00:00Z",
          "events": [{"code": "COLLECTED", "time": "2026-09-25T09:00:00Z", "city": "Rotterdam", "country": "NL"}],
          "proofOfDelivery": {"receivedBy": "J. Becker", "deliveredAt": "2026-09-25T18:00:00Z", "condition": "RESERVATIONS"}
        }
        """;

    private readonly StubHandler _partnerApi = new();
    private readonly IPersonaTokens _tokens = Substitute.For<IPersonaTokens>();
    private readonly FakeLogger<NorviaPartnerApiClient> _logger = new();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly HttpClient _http;

    public ConsigneeTrackingClientTests() => _http = new HttpClient(_partnerApi) { BaseAddress = new Uri("https://apim.example/partner/v1/") };

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _partnerApi.Dispose();
    }

    [Fact]
    public async Task Get_AsksTheConsigneeApiWithTheProductsKeyAndNoToken_AndTheCodeInAHeader()
    {
        _partnerApi.Answer(HttpStatusCode.OK, View, "corr-1");

        var answer = await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        var request = _partnerApi.Request!;
        request.RequestUri.Should().Be(new Uri("https://apim.example/consignee/v1/view"));
        request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("consignee-key");
        request.Headers.GetValues("X-Tracking-Code").Should().Equal(Code);
        request.Headers.GetValues("X-Client-IP").Should().Equal("203.0.113.9");
        request.Headers.Authorization.Should().BeNull();
        await _tokens.DidNotReceive().GetTokenAsync(Arg.Any<Norvia.PartnerPortal.Domain.Personas.PersonaId>(), Arg.Any<CancellationToken>());
        answer.IsFound.Should().BeTrue();
        answer.MessageId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Get_ReadsTheViewWithItsTimesAsInstants()
    {
        _partnerApi.Answer(HttpStatusCode.OK, View);

        var view = (await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken)).Value!;

        view.Status.Should().Be(ShipperTrackingStatus.InTransit);
        view.Receiver.Should().Be(new ShipperPlace("Utrecht", "NL"));
        view.Eta.Should().Be(new DateTimeOffset(2026, 9, 25, 19, 0, 0, TimeSpan.Zero));
        view.LastUpdate.Should().Be(new DateTimeOffset(2026, 9, 25, 11, 0, 0, TimeSpan.Zero));
        view.Progress.Should().ContainSingle().Which.Type.Should().Be(ShipperProgressType.Collected);
        view.ProofOfDelivery!.Condition.Should().Be(ProofOfDeliveryCondition.Reservations);
    }

    [Fact]
    public async Task Get_ACodeNorviaDoesNotKnow_IsNotFound()
    {
        _partnerApi.Answer(HttpStatusCode.NotFound, """{"code":"TrackingCodeNotFound","text":"We can't find this shipment."}""");

        var answer = await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(TrackingOutcome.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, TrackingOutcome.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TrackingOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TrackingOutcome.Unavailable)]
    public async Task Get_AFailure_IsAnAnswerNotAnError(HttpStatusCode status, TrackingOutcome expected)
    {
        _partnerApi.Answer(status, "{}");

        var answer = await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Get_AnAnswerInNoKnownFormat_IsUnavailable()
    {
        _partnerApi.Answer(HttpStatusCode.OK, """{"status":"TELEPORTED","events":[]}""");

        var answer = await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(TrackingOutcome.Unavailable);
    }

    [Fact]
    public async Task Get_WithNoKeyConfigured_IsUnavailable_AndCallsNothing()
    {
        var answer = await Client(key: null).GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(TrackingOutcome.Unavailable);
        _partnerApi.Request.Should().BeNull();
    }

    [Fact]
    public async Task Get_NeverLogsTheCode()
    {
        _partnerApi.Answer(HttpStatusCode.OK, View, "corr-1");
        await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);
        _partnerApi.Answer(HttpStatusCode.NotFound, "{}");
        await Client().GetConsigneeViewAsync(Code, "203.0.113.9", TestContext.Current.CancellationToken);

        var logged = _logger.Collector.GetSnapshot()
            .Select(log => log.Message + string.Join(';', log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).Select(pair => $"{pair.Key}={pair.Value}")))
            .ToList();
        logged.Should().NotBeEmpty().And.NotContain(text => text.Contains(Code, StringComparison.Ordinal));
        logged.Should().Contain(text => text.Contains("flow=consignee-tracking", StringComparison.Ordinal));
    }

    private NorviaPartnerApiClient Client(string? key = "consignee-key") => new(
        _http,
        _tokens,
        Options.Create(new PartnerApiOptions { BaseAddress = _http.BaseAddress, PersonaClientIds = "brouwer-dranken=client-1", ConsigneeSubscriptionKey = key }),
        _activities,
        Substitute.For<IPartnerPortalFaults>(),
        _logger);
}
