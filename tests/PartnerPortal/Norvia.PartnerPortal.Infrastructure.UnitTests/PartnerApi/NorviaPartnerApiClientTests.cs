using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class NorviaPartnerApiClientTests : IDisposable
{
    private readonly StubHandler _partnerApi = new();
    private readonly IPersonaTokens _tokens = Substitute.For<IPersonaTokens>();
    private readonly FakeLogger<NorviaPartnerApiClient> _logger = new();
    private readonly PortalActivities _activities = new("Norvia.PartnerPortal.Web.Tests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaPartnerApiClient _client;

    public NorviaPartnerApiClientTests()
    {
        _http = new HttpClient(_partnerApi) { BaseAddress = new Uri("https://apim.example/partner/v1/") };
        _tokens.GetTokenAsync(Samples.BrouwerDranken.Id, Arg.Any<CancellationToken>()).Returns("brouwer-token");
        var options = Options.Create(new PartnerApiOptions
        {
            BaseAddress = _http.BaseAddress,
            PersonaClientIds = "brouwer-dranken=client-1;van-dijk-transport=client-2",
            SubscriptionKeys = new() { ["brouwer-dranken"] = "brouwer-key", ["van-dijk-transport"] = "van-dijk-key" },
        });
        _client = new NorviaPartnerApiClient(_http, _tokens, options, _activities, _logger);
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

    [Fact]
    public async Task SubmitAsync_PostsTheBookingToTransportOrders_AsThePersona()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        await SubmitAsync(Samples.FullBooking());

        _partnerApi.Request!.Method.Should().Be(HttpMethod.Post);
        _partnerApi.Request.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/transport-orders"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("brouwer-key");
        _partnerApi.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _partnerApi.Request.Headers.Authorization.Parameter.Should().Be("brouwer-token");
    }

    [Fact]
    public async Task SubmitAsync_FullBooking_SendsItInThePartnerFormat()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        await SubmitAsync(Samples.FullBooking());

        var sent = JsonNode.Parse(_partnerApi.RequestBody!);
        JsonNode.DeepEquals(sent, JsonNode.Parse("""
            {
              "bookingRef": "BD-2030-0002",
              "pickupStreet": "Waalhaven Oostzijde 88",
              "pickupPostalCode": "3087 BM",
              "pickupCity": "Rotterdam",
              "pickupCountry": "NL",
              "pickupFrom": "02-10-2030 07:30",
              "pickupUntil": "02-10-2030 11:00",
              "receiverName": "Getränke Schmitz GmbH",
              "receiverContact": "Jonas Schmitz",
              "receiverPhone": "+49 211 555 0190",
              "receiverEmail": "wareneingang@getraenke-schmitz.example",
              "receiverStreet": "Kölner Landstraße 301",
              "receiverPostalCode": "40591",
              "receiverCity": "Düsseldorf",
              "receiverCountry": "DE",
              "deliverFrom": "03-10-2030 08:00",
              "deliverUntil": "03-10-2030 16:30",
              "items": [
                { "packaging": "PX", "count": 4, "weight": "2400 kg", "dimensions": "120x80x160 cm" },
                { "packaging": "CR", "count": 10, "weight": "180.25 kg", "dimensions": "60x40x35.5 cm" },
                { "packaging": "PK", "count": 3, "weight": "12 kg" },
                { "packaging": "NE", "count": 1, "weight": "35 kg" }
              ],
              "remarks": "Deliver at dock 3. Call Jonas 30 minutes before arrival."
            }
            """)).Should().BeTrue(sent!.ToJsonString());
        Contracts.BookingSchemaErrors(sent).Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_SendsTheBookingWithItsLength_BecauseTheIntakeLogicAppGetsNoBodyFromAChunkedOne()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        await SubmitAsync(Samples.FullBooking());

        _partnerApi.RequestContentLength.Should().Be(System.Text.Encoding.UTF8.GetByteCount(_partnerApi.RequestBody!));
    }

    [Fact]
    public async Task SubmitAsync_EmptyBooking_LeavesOutWhatIsEmpty_SoNorviaNamesWhatIsMissing()
    {
        _partnerApi.Answer(HttpStatusCode.UnprocessableEntity, Contracts.Read("PartnerApi/samples/responses/rejected.json"));

        await SubmitAsync(new BookingRequest
        {
            ShipperReference = " ",
            CollectionAddress = new BookingAddress(Street: "Waalhaven Oostzijde 88", City: ""),
            GoodsLines = [new BookingGoodsLine(PackageType: null, Quantity: null, WeightKg: null)],
        });

        var sent = JsonNode.Parse(_partnerApi.RequestBody!);
        JsonNode.DeepEquals(sent, JsonNode.Parse("""{ "pickupStreet": "Waalhaven Oostzijde 88", "items": [{}] }""")).Should().BeTrue(sent!.ToJsonString());
        Contracts.BookingSchemaErrors(sent).Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_Accepted_IsBookedUnderTheTransportOrderNumber()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.Accepted("TO-0000042", "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31"));
    }

    [Fact]
    public async Task SubmitAsync_AlreadyBooked_IsBookedUnderTheOriginalNumber()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/responses/already-booked.json"));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.AlreadyBooked("TO-0000042", "c81e5d07-3a2f-4f6b-9e14-0b7a9d2c6e58"));
    }

    [Fact]
    public async Task SubmitAsync_Rejected_KeepsEveryReason()
    {
        _partnerApi.Answer(HttpStatusCode.UnprocessableEntity, Contracts.Read("PartnerApi/samples/responses/rejected.json"));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Result.Should().Be(BookingResult.Rejected);
        answer.MessageId.Should().Be("5a9e2b14-6c3d-47f1-8b20-d4e7f1a0c963");
        answer.Reasons.Should().HaveCount(5).And.StartWith(new RejectionReason("DeliveryAddressIncomplete", "The delivery address has no country."));
    }

    [Fact]
    public async Task SubmitAsync_NotReceived_IsNotReceived_WithNorviasMessageId()
    {
        _partnerApi.Answer(HttpStatusCode.BadGateway, Contracts.Read("PartnerApi/samples/responses/not-received.json"));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.NotReceived("e2d4f6a8-1b3c-4d5e-8f70-9a1b2c3d4e5f"));
    }

    [Fact]
    public async Task SubmitAsync_ForbiddenAtTheEdge_IsRefusedInNorviasWords()
    {
        _partnerApi.Answer(HttpStatusCode.Forbidden, """{ "messageId": "corr-403", "text": "This client may not submit Transport Orders." }""", correlationId: "corr-403");

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-403"));
    }

    [Fact]
    public async Task SubmitAsync_UnauthorizedAtTheEdge_IsRefused_WithTheCorrelationIdFromTheHeader()
    {
        _partnerApi.Answer(HttpStatusCode.Unauthorized, """{ "statusCode": 401, "message": "Access denied due to invalid subscription key." }""", correlationId: "corr-401");

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Result.Should().Be(BookingResult.Refused);
        answer.Text.Should().Be("Norvia didn't recognise this Persona's subscription key or token.");
        answer.MessageId.Should().Be("corr-401");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>Service unavailable</html>")]
    [InlineData(HttpStatusCode.Created, """{ "result": "ACCEPTED" }""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{ "result": "REJECTED", "errors": [] }""")]
    [InlineData(HttpStatusCode.OK, """{ "result": "SOMETHING_NEW", "norviaOrderNo": "TO-0000042" }""")]
    public async Task SubmitAsync_AnswerThatSaysNothingWasBooked_IsNotReceived(HttpStatusCode status, string body)
    {
        _partnerApi.Answer(status, body, correlationId: "corr-5xx");

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.NotReceived("corr-5xx"));
    }

    [Fact]
    public async Task SubmitAsync_NorviaUnreachable_IsNotReceived()
    {
        _partnerApi.Fail(new HttpRequestException("Connection refused"));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.NotReceived(null));
    }

    [Fact]
    public async Task SubmitAsync_NorviaTooSlow_IsNotReceived()
    {
        _partnerApi.Fail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.NotReceived(null));
    }

    [Fact]
    public async Task SubmitAsync_NoTokenForThePersona_IsNotReceived_AndSendsNothing()
    {
        _tokens.GetTokenAsync(Samples.BrouwerDranken.Id, Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("Entra said no."));

        var answer = await SubmitAsync(Samples.FullBooking());

        answer.Should().Be(NorviaAnswer.NotReceived(null));
        _partnerApi.Request.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_Answered_LogsTheTrackingKeys_SoTheTraceJoinsNorviasMessageTrail()
    {
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        await SubmitAsync(Samples.FullBooking());

        var scope = _logger.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
        scope.Should().Contain("flow", "transport-order-intake")
            .And.Contain("partyId", "SHP-0001")
            .And.Contain("correlationId", "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31")
            .And.Contain("transportOrderNumber", "TO-0000042");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, "PartnerApi/samples/responses/accepted.json")]
    [InlineData(HttpStatusCode.OK, "PartnerApi/samples/responses/already-booked.json")]
    public async Task SubmitAsync_Booked_RecordsItSucceeded(HttpStatusCode status, string answer)
    {
        _partnerApi.Answer(status, Contracts.Read(answer));

        await SubmitAsync(Samples.FullBooking());

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Succeeded")
            .And.NotContainKey("failureCode", "a key that doesn't apply is left out, never sent empty");
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "PartnerApi/samples/responses/rejected.json", "Refused", "TransportOrderRejected")]
    [InlineData(HttpStatusCode.BadGateway, "PartnerApi/samples/responses/not-received.json", "Failed", "BookingNotReceived")]
    public async Task SubmitAsync_NotBooked_RecordsTheOutcome_WithItsFailureCode_AsTheIntakesOtherHopsDo(
        HttpStatusCode status, string answer, string outcome, string failureCode)
    {
        _partnerApi.Answer(status, Contracts.Read(answer));

        await SubmitAsync(Samples.FullBooking());

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", outcome).And.Contain("failureCode", failureCode);
    }

    [Fact]
    public async Task SubmitAsync_RefusedAtTheEdge_RecordsItRefused()
    {
        _partnerApi.Answer(HttpStatusCode.Forbidden, """{ "messageId": "corr-403", "text": "This client may not submit Transport Orders." }""", correlationId: "corr-403");

        await SubmitAsync(Samples.FullBooking());

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Refused").And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task SubmitAsync_SendsTheBookingInASpanOfItsOwn_WhichTheAnswersLogIsWrittenIn()
    {
        // The Integration console names the portal's hop after it, not after Blazor's span for the form's submit event.
        _partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));

        await SubmitAsync(Samples.FullBooking());

        var span = _spans.Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("Send booking");
        _logger.LatestRecord.Timestamp.UtcDateTime.Should().BeOnOrAfter(span.StartTimeUtc).And.BeOnOrBefore(span.StartTimeUtc + span.Duration);
    }

    [Fact]
    public async Task SubmitAsync_CancelledByTheCaller_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _partnerApi.Fail(new TaskCanceledException());

        var submit = () => _client.SubmitAsync(Samples.BrouwerDranken, Samples.FullBooking(), cancelled.Token);

        await submit.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();

    private static readonly CarrierAnswer Confirmation = new CarrierAnswer.Confirmation("VDT-77812", new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2)));

    private Task<NorviaReply> AnswerAsync(CarrierAnswer answer, string number = "CB-0000042") =>
        _client.AnswerAsync(Samples.VanDijkTransport, number, "SH-0000007", answer, CancellationToken.None);

    [Fact]
    public async Task AnswerAsync_PostsTheAnswerToCarrierAnswers_AsThePersona()
    {
        _tokens.GetTokenAsync(Samples.VanDijkTransport.Id, Arg.Any<CancellationToken>()).Returns("van-dijk-token");
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(Confirmation);

        _partnerApi.Request!.Method.Should().Be(HttpMethod.Post);
        _partnerApi.Request.RequestUri.Should().Be(new Uri("https://apim.example/partner/v1/carrier-answers"));
        _partnerApi.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").Should().Equal("van-dijk-key");
        _partnerApi.Request.Headers.Authorization!.Parameter.Should().Be("van-dijk-token");
        _partnerApi.RequestContentLength.Should().Be(System.Text.Encoding.UTF8.GetByteCount(_partnerApi.RequestBody!));
    }

    [Fact]
    public async Task AnswerAsync_AConfirmation_IsSentInTheCarrierAnswerFormat_WithoutNamingTheCarrier()
    {
        _tokens.GetTokenAsync(Samples.VanDijkTransport.Id, Arg.Any<CancellationToken>()).Returns("van-dijk-token");
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(Confirmation);

        var sent = JsonNode.Parse(_partnerApi.RequestBody!);
        JsonNode.DeepEquals(sent, JsonNode.Parse("""
            { "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "VDT-77812", "plannedCollection": "2030-10-02T08:30:00+02:00" }
            """)).Should().BeTrue(sent!.ToJsonString());
        Contracts.CarrierAnswerSchemaErrors(sent).Should().BeEmpty();
        sent!.ToJsonString().Should().NotContain("CAR-0001");
    }

    [Theory]
    [InlineData(CarrierRejectionReason.NoCapacity, null, """{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "NO_CAPACITY" }""")]
    [InlineData(CarrierRejectionReason.LaneNotServed, "Not our lane", """{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "LANE_NOT_SERVED", "declineText": "Not our lane" }""")]
    [InlineData(CarrierRejectionReason.WindowNotFeasible, null, """{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "WINDOW_NOT_FEASIBLE" }""")]
    [InlineData(CarrierRejectionReason.GoodsNotAccepted, null, """{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "GOODS_NOT_ACCEPTED" }""")]
    [InlineData(CarrierRejectionReason.Other, "No trucks on Friday", """{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "OTHER", "declineText": "No trucks on Friday" }""")]
    public async Task AnswerAsync_ARejection_IsSentWithTheCarriersOwnReasonCode(CarrierRejectionReason reason, string? text, string expected)
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(new CarrierAnswer.Rejection(reason, text));

        var sent = JsonNode.Parse(_partnerApi.RequestBody!);
        JsonNode.DeepEquals(sent, JsonNode.Parse(expected)).Should().BeTrue(sent!.ToJsonString());
        Contracts.CarrierAnswerSchemaErrors(sent).Should().BeEmpty();
    }

    [Fact]
    public async Task AnswerAsync_AConfirmationWithoutAPlannedTime_LeavesItOut()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(new CarrierAnswer.Confirmation("VDT-1"));

        JsonNode.Parse(_partnerApi.RequestBody!)!.AsObject().ContainsKey("plannedCollection").Should().BeFalse();
    }

    [Fact]
    public async Task AnswerAsync_Recorded_IsRecorded_WithNorviasMessageId()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        var reply = await AnswerAsync(Confirmation);

        reply.Should().Be(NorviaReply.Recorded("5b1c2a52-6a52-4d8e-9d52-0d8f6f3f7b11"));
    }

    [Fact]
    public async Task AnswerAsync_RecordedBefore_IsRecordedAndRepeated()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded-repeated.json"));

        var reply = await AnswerAsync(Confirmation);

        reply.Repeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "booking-not-open.json", CarrierAnswerResult.BookingNotOpen)]
    [InlineData(HttpStatusCode.NotFound, "booking-not-found.json", CarrierAnswerResult.BookingNotFound)]
    [InlineData(HttpStatusCode.Forbidden, "not-allowed.json", CarrierAnswerResult.NotAllowed)]
    [InlineData(HttpStatusCode.BadRequest, "invalid.json", CarrierAnswerResult.Invalid)]
    public async Task AnswerAsync_ARefusalOfNorvia_IsKeptInNorviasWords(HttpStatusCode status, string sample, CarrierAnswerResult result)
    {
        var body = Contracts.Read($"PartnerApi/samples/carrier-answer-responses/{sample}");
        _partnerApi.Answer(status, body);

        var reply = await AnswerAsync(Confirmation);

        reply.Result.Should().Be(result);
        reply.Text.Should().Be(JsonNode.Parse(body)!["text"]!.GetValue<string>());
        reply.MessageId.Should().Be(JsonNode.Parse(body)!["messageId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "{}", CarrierAnswerResult.NotReceived)]
    [InlineData(HttpStatusCode.OK, """{ "result": "RECORDED" }""", CarrierAnswerResult.Recorded)]
    [InlineData(HttpStatusCode.BadGateway, "", CarrierAnswerResult.NotReceived)]
    [InlineData(HttpStatusCode.InternalServerError, "<html>boom</html>", CarrierAnswerResult.NotReceived)]
    [InlineData(HttpStatusCode.OK, """{ "result": "SOMETHING_NEW" }""", CarrierAnswerResult.NotReceived)]
    public async Task AnswerAsync_AReplyThatDoesntSayItWasRecorded_IsNotReceived(HttpStatusCode status, string body, CarrierAnswerResult expected)
    {
        _partnerApi.Answer(status, body, correlationId: "corr-x");

        var reply = await AnswerAsync(Confirmation);

        reply.Result.Should().Be(expected);
        reply.MessageId.Should().Be("corr-x");
    }

    [Fact]
    public async Task AnswerAsync_Refused403AtTheEdge_IsRefusedInNorviasWords()
    {
        _partnerApi.Answer(HttpStatusCode.Forbidden, """{ "messageId": "corr-403", "text": "This client may not answer Carrier bookings." }""", correlationId: "corr-403");

        var reply = await AnswerAsync(Confirmation);

        reply.Should().Be(NorviaReply.Refusal(CarrierAnswerResult.Refused, "This client may not answer Carrier bookings.", "corr-403"));
    }

    [Fact]
    public async Task AnswerAsync_UnauthorizedAtTheEdge_IsRefused()
    {
        _partnerApi.Answer(HttpStatusCode.Unauthorized, """{ "statusCode": 401 }""", correlationId: "corr-401");

        var reply = await AnswerAsync(Confirmation);

        reply.Result.Should().Be(CarrierAnswerResult.Refused);
        reply.Text.Should().Be(PartnerAnswers.UnrecognisedCredentials);
    }

    [Fact]
    public async Task AnswerAsync_NorviaUnreachable_IsNotReceived()
    {
        _partnerApi.Fail(new HttpRequestException("Connection refused"));

        (await AnswerAsync(Confirmation)).Should().Be(NorviaReply.NotReceived(null));
    }

    [Fact]
    public async Task AnswerAsync_NoTokenForThePersona_IsNotReceived_AndSendsNothing()
    {
        _tokens.GetTokenAsync(Samples.VanDijkTransport.Id, Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("Entra said no."));

        (await AnswerAsync(Confirmation)).Should().Be(NorviaReply.NotReceived(null));
        _partnerApi.Request.Should().BeNull();
    }

    [Fact]
    public async Task AnswerAsync_Replied_LogsTheTrackingKeys_SoTheTraceJoinsNorviasMessageTrail()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(Confirmation);

        ScopeOf(_logger.LatestRecord).Should().Contain("flow", "carrier-answer")
            .And.Contain("partyId", "CAR-0001")
            .And.Contain("carrierBookingNumber", "CB-0000042")
            .And.Contain("shipmentNumber", "SH-0000007")
            .And.Contain("correlationId", "5b1c2a52-6a52-4d8e-9d52-0d8f6f3f7b11")
            .And.Contain("outcome", "Succeeded")
            .And.NotContainKey("failureCode");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "booking-not-open.json", "Refused", "BookingNotOpen")]
    [InlineData(HttpStatusCode.NotFound, "booking-not-found.json", "Refused", "BookingNotFound")]
    [InlineData(HttpStatusCode.Forbidden, "not-allowed.json", "Refused", "CarrierNotAllowed")]
    [InlineData(HttpStatusCode.BadRequest, "invalid.json", "Refused", "CarrierAnswerUnreadable")]
    [InlineData(HttpStatusCode.BadGateway, "not-received.json", "Failed", "AnswerNotReceived")]
    public async Task AnswerAsync_NotRecorded_RecordsTheOutcomeWithItsFailureCode(HttpStatusCode status, string sample, string outcome, string failureCode)
    {
        _partnerApi.Answer(status, Contracts.Read($"PartnerApi/samples/carrier-answer-responses/{sample}"));

        await AnswerAsync(Confirmation);

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", outcome).And.Contain("failureCode", failureCode);
    }

    [Fact]
    public async Task AnswerAsync_RefusedAtTheEdge_RecordsItRefused_WithoutAFailureCode()
    {
        _partnerApi.Answer(HttpStatusCode.Forbidden, """{ "messageId": "corr-403", "text": "No." }""", correlationId: "corr-403");

        await AnswerAsync(Confirmation);

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Refused").And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task AnswerAsync_NorviaNeverReplied_LogsNoCorrelationId()
    {
        _partnerApi.Fail(new HttpRequestException("Connection refused"));

        await AnswerAsync(Confirmation);

        ScopeOf(_logger.LatestRecord).Should().NotContainKey("correlationId");
    }

    [Fact]
    public async Task AnswerAsync_SendsTheAnswerInASpanOfItsOwn()
    {
        _partnerApi.Answer(HttpStatusCode.OK, Contracts.Read("PartnerApi/samples/carrier-answer-responses/recorded.json"));

        await AnswerAsync(Confirmation);

        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Send Carrier answer");
    }

    [Fact]
    public async Task AnswerAsync_WithoutAPersona_Throws()
    {
        var answer = () => _client.AnswerAsync(null!, "CB-1", "SH-1", Confirmation, CancellationToken.None);

        await answer.Should().ThrowAsync<ArgumentNullException>();
    }

    private Task<NorviaAnswer> SubmitAsync(BookingRequest booking) =>
        _client.SubmitAsync(Samples.BrouwerDranken, booking, TestContext.Current.CancellationToken);
}
