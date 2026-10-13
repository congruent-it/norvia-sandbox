using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class NorviaPartnerApiClientTests : IDisposable
{
    private readonly StubHandler _partnerApi = new();
    private readonly IPersonaTokens _tokens = Substitute.For<IPersonaTokens>();
    private readonly FakeLogger<NorviaPartnerApiClient> _logger = new();
    private readonly SimulatorActivities _activities = new("Norvia.Simulator.Web.Tests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaPartnerApiClient _client;

    public NorviaPartnerApiClientTests()
    {
        _http = new HttpClient(_partnerApi) { BaseAddress = new Uri("https://apim.example/partner/v1/") };
        _tokens.GetTokenAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns("brouwer-token");
        var options = Options.Create(new PartnerApiOptions
        {
            BaseAddress = _http.BaseAddress,
            PersonaClientIds = "brouwer-dranken=client-1",
            SubscriptionKeys = new() { ["brouwer-dranken"] = "brouwer-key" },
        });
        _client = new NorviaPartnerApiClient(_http, _tokens, options, _activities, new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 7, 30, 0, TimeSpan.Zero)), _logger);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _activities.Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
        _partnerApi.Dispose();
    }

    private Task<BookingAnswer> SendAsync(PlannedBooking? booking = null) =>
        _client.SendBookingAsync(booking ?? TestPlans.Good(), CancellationToken.None);

    [Fact]
    public async Task SendBooking_PostsToTransportOrders_AsThePersona()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """{"result":"ACCEPTED","norviaOrderNo":"TO-1"}""", "corr-1");

        await SendAsync();

        _partnerApi.Request!.Method.Should().Be(HttpMethod.Post);
        _partnerApi.Request.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/transport-orders"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("brouwer-key");
        _partnerApi.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _partnerApi.Request.Headers.Authorization.Parameter.Should().Be("brouwer-token");
    }

    [Fact]
    public async Task SendBooking_SendsTheBookingInThePartnerFormat_WithItsLength()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """{"result":"ACCEPTED","norviaOrderNo":"TO-1"}""");
        var booking = TestPlans.Good();

        await SendAsync(booking);

        var sent = JsonNode.Parse(_partnerApi.RequestBody!)!;
        sent["bookingRef"]!.GetValue<string>().Should().Be(booking.Reference);
        _partnerApi.RequestContentLength.Should().BeGreaterThan(0, "a chunked body gets no body in the intake Logic App's trigger");
    }

    [Fact]
    public async Task SendBooking_Accepted_GivesTheOrderNumberAndTheCorrelationIdOfTheHeader()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """{"result":"ACCEPTED","norviaOrderNo":"TO-0000009"}""", "corr-9");

        var answer = await SendAsync();

        answer.Outcome.Should().Be(LogOutcome.Accepted);
        answer.TransportOrderNumber.Should().Be("TO-0000009");
        answer.CorrelationId.Should().Be("corr-9");
    }

    [Fact]
    public async Task SendBooking_NorviaUnreachable_IsNotReceived_NotAnError()
    {
        _partnerApi.Fail(new HttpRequestException("down"));

        var answer = await SendAsync();

        answer.Outcome.Should().Be(LogOutcome.NotReceived);
        answer.CorrelationId.Should().BeNull();
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task SendBooking_NoSubscriptionKeyForThePersona_IsNotReceived()
    {
        var booking = TestPlans.Good() with { PersonaId = "someone-else" };

        var answer = await SendAsync(booking);

        answer.Outcome.Should().Be(LogOutcome.NotReceived);
    }

    [Fact]
    public async Task SendBooking_SendsInASpanOfItsOwn_NamedAfterTheAction()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """{"result":"ACCEPTED","norviaOrderNo":"TO-1"}""");

        await SendAsync();

        var span = _spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be("Send booking");
        span.Kind.Should().Be(ActivityKind.Internal);
    }

    [Fact]
    public async Task SendBooking_LogsTheAnswerWithTheTrackingKeys_AndNeverWhatWasBooked()
    {
        _partnerApi.Answer(HttpStatusCode.Created, """{"result":"ACCEPTED","norviaOrderNo":"TO-0000009"}""", "corr-9");
        var booking = TestPlans.Good();

        await SendAsync(booking);

        var record = _logger.Collector.GetSnapshot().Should().ContainSingle(entry => entry.Message.Contains("Norvia answered", StringComparison.Ordinal)).Subject;
        var scope = _logger.Collector.GetSnapshot().SelectMany(entry => entry.Scopes).OfType<IEnumerable<KeyValuePair<string, object?>>>().Single().ToDictionary(pair => pair.Key, pair => pair.Value);
        scope["flow"].Should().Be("transport-order-intake");
        scope["partyId"].Should().Be("SHP-0001");
        scope["correlationId"].Should().Be("corr-9");
        scope["transportOrderNumber"].Should().Be("TO-0000009");
        scope["outcome"].Should().Be("Succeeded");
        record.Message.Should().NotContain(booking.ConsigneeName!);
    }

    [Theory]
    [InlineData(LogOutcome.Accepted, "Succeeded", null)]
    [InlineData(LogOutcome.AlreadyBooked, "Succeeded", null)]
    [InlineData(LogOutcome.Rejected, "Refused", "TransportOrderRejected")]
    [InlineData(LogOutcome.Refused, "Refused", null)]
    [InlineData(LogOutcome.NotReceived, "Failed", "BookingNotReceived")]
    public void TrackingKeysOf_ReadsTheAnswerAsTheIntakesOtherHopsDo(LogOutcome outcome, string expected, string? failureCode)
    {
        var keys = NorviaPartnerApiClient.TrackingKeysOf(TestPlans.Good(), new BookingAnswer(outcome, null, null, null));

        keys["outcome"].Should().Be(expected);
        keys.GetValueOrDefault("failureCode").Should().Be(failureCode);
        keys.Should().NotContainKey("correlationId", "a key that doesn't apply is left out, never sent empty");
    }

    [Fact]
    public void TrackingKeysOf_AnInfoLine_IsNotAnAnswer()
    {
        var act = () => NorviaPartnerApiClient.TrackingKeysOf(TestPlans.Good(), new BookingAnswer(LogOutcome.Info, null, null, null));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
