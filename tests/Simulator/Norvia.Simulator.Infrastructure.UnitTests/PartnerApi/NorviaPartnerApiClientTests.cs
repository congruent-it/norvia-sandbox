using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;
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
        _tokens.GetTokenAsync("van-dijk-transport", Arg.Any<CancellationToken>()).Returns("van-dijk-token");
        var options = Options.Create(new PartnerApiOptions
        {
            BaseAddress = _http.BaseAddress,
            PersonaClientIds = "brouwer-dranken=client-1;van-dijk-transport=client-2",
            SubscriptionKeys = new() { ["brouwer-dranken"] = "brouwer-key", ["van-dijk-transport"] = "van-dijk-key" },
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

    private static readonly SimulatedCarrier VanDijk = new("CAR-0001", "Van Dijk Transport", true, "van-dijk-transport", "Van Dijk Transport", "VDT");
    private static readonly DateTimeOffset At = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);
    private static readonly PlannedAddress Rotterdam = new("Coolsingel 1", "3011 AD", "Rotterdam", "NL");

    [Fact]
    public async Task AnswerBooking_PostsToCarrierAnswers_AsTheCarriersPersona()
    {
        _partnerApi.Answer(HttpStatusCode.OK, """{"result":"RECORDED","messageId":"m-1","carrierBookingNumber":"CB-0000042","decision":"CONFIRMED","repeated":false}""");

        var answer = await _client.AnswerBookingAsync(VanDijk, new CarrierAnswerRequest("CB-0000042", true, "VDT-SIM1-3", null, null), CancellationToken.None);

        _partnerApi.Request!.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/carrier-answers"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("van-dijk-key");
        _partnerApi.Request.Headers.Authorization!.Parameter.Should().Be("van-dijk-token");
        _partnerApi.RequestContentLength.Should().BeGreaterThan(0);
        JsonNode.Parse(_partnerApi.RequestBody!)!["decision"]!.GetValue<string>().Should().Be("CONFIRMED");
        answer.Outcome.Should().Be(LogOutcome.Confirmed);
        answer.CorrelationId.Should().Be("m-1");
    }

    [Fact]
    public async Task SendTracking_PostsToCarrierTracking_AndNorviaAcceptsItForLater()
    {
        _partnerApi.Answer(HttpStatusCode.Accepted, """{"messageId":"m-2"}""", "m-2");

        var answer = await _client.SendTrackingAsync(VanDijk, new CarrierTrackingRequest("VDT-SIM1-3", "CB-0000042", "E-1", CarrierEvent.Collected, At, Rotterdam, null), CancellationToken.None);

        _partnerApi.Request!.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/carrier-tracking"));
        _partnerApi.Request.Headers.Authorization!.Parameter.Should().Be("van-dijk-token");
        JsonNode.Parse(_partnerApi.RequestBody!)!["statusCode"]!.GetValue<string>().Should().Be("PU");
        answer.Should().Be(new CarrierCallAnswer(LogOutcome.TrackingSent, "m-2", null));
    }

    [Fact]
    public async Task SendProofOfDelivery_PostsToCarrierProofsOfDelivery()
    {
        _partnerApi.Answer(HttpStatusCode.Accepted, """{"messageId":"m-3"}""");

        var answer = await _client.SendProofOfDeliveryAsync(VanDijk, new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "VDT-SIM1-3", "CB-0000042", "D-1", "Someone", At, Rotterdam, false, null), CancellationToken.None);

        _partnerApi.Request!.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/carrier-proofs-of-delivery"));
        answer.Outcome.Should().Be(LogOutcome.ProofOfDeliverySent);
    }

    [Fact]
    public async Task ACarrierCall_NorviaUnreachable_IsNotReceived()
    {
        _partnerApi.Fail(new HttpRequestException("down"));

        var answer = await _client.SendTrackingAsync(VanDijk, new CarrierTrackingRequest("R", "CB-0000042", "E-1", CarrierEvent.Arrived, At, Rotterdam, null), CancellationToken.None);

        answer.Outcome.Should().Be(LogOutcome.CarrierNotReceived);
        answer.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task ACarrierCall_ACarrierWithoutAPersona_IsRefusedBeforeAnythingIsSent()
    {
        var act = () => _client.SendTrackingAsync(new SimulatedCarrier("CAR-0009", "Nobody", true), new CarrierTrackingRequest("R", "CB-0000042", "E-1", CarrierEvent.Arrived, At, Rotterdam, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CAR-0009*");
    }

    [Fact]
    public async Task ACarrierCall_IsSentInASpanNamedForWhatItDoes_AndLoggedWithTheKeysTheSimulatorKnows()
    {
        _partnerApi.Answer(HttpStatusCode.Accepted, """{"messageId":"m-4"}""");

        await _client.SendProofOfDeliveryAsync(VanDijk, new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "R", "CB-0000042", "D-1", "Someone", At, Rotterdam, false, null), CancellationToken.None);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Send Proof of delivery");
        var keys = _logger.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary(pair => pair.Key, pair => pair.Value);
        keys["flow"].Should().Be("carrier-proof-of-delivery");
        keys["partyId"].Should().Be("CAR-0001");
        keys["carrierBookingNumber"].Should().Be("CB-0000042");
        keys["outcome"].Should().Be("Succeeded");
        keys["correlationId"].Should().Be("m-4");
    }

    [Theory]
    [InlineData(LogOutcome.Confirmed, "Succeeded", null)]
    [InlineData(LogOutcome.Declined, "Succeeded", null)]
    [InlineData(LogOutcome.TrackingSent, "Succeeded", null)]
    [InlineData(LogOutcome.CarrierRefused, "Refused", null)]
    [InlineData(LogOutcome.CarrierNotReceived, "Failed", "CarrierCallNotReceived")]
    public void CarrierTrackingKeysOf_FollowsTheOutcome(LogOutcome outcome, string expected, string? failureCode)
    {
        var keys = NorviaPartnerApiClient.CarrierTrackingKeysOf("carrier-answer", "CAR-0001", "CB-0000042", new CarrierCallAnswer(outcome, null, null));

        keys["outcome"].Should().Be(expected);
        keys.GetValueOrDefault("failureCode").Should().Be(failureCode);
        keys.Should().NotContainKey("correlationId", "a key that doesn't apply is left out");
    }
}
