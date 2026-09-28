using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

public sealed class TmsApiClientTests : IDisposable
{
    private readonly StubHandler _tmsApi = new();
    private readonly ITmsApiTokens _tokens = Substitute.For<ITmsApiTokens>();
    private readonly FakeLogger<TmsApiClient> _logger = new();
    private readonly TmsUiActivities _activities = new("Norvia.Tms.Web.Tests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly TmsApiClient _client;

    public TmsApiClientTests()
    {
        _http = new HttpClient(_tmsApi) { BaseAddress = new Uri("http://tms-api.example") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tms-ui-token");
        _client = new TmsApiClient(_http, _tokens, _activities, _logger);
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
        _tmsApi.Dispose();
    }

    [Fact]
    public async Task ReRateAsync_PostsToTheOrdersReRate_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/transport-orders/TO-0000043/re-rate"));
        _tmsApi.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _tmsApi.Request.Headers.Authorization.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReRateAsync_TwoReRates_GoUnderKeysOfTheirOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("NoMatchingLane", "No Lane.")));
        await ReRateAsync("TO-0000043");
        var first = _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single();

        await ReRateAsync("TO-0000043");

        _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single().Should().NotBe(first);
    }

    [Fact]
    public async Task ReRateAsync_AnsweredRated_IsRated()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Rated());
    }

    [Fact]
    public async Task ReRateAsync_AnsweredRatingFailed_FailedAgainWithTheNewFailure()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.FailedAgain(new RatingFailureView("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));
    }

    [Fact]
    public async Task ReRateAsync_Conflict_IsRefusedWithTheTmsReason()
    {
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409,
              "title": "The Transport Order can't be re-rated.",
              "detail": "Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."
            }
            """,
            "application/problem+json");

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Refused("Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."));
    }

    [Fact]
    public async Task ReRateAsync_NotFound_IsRefusedAsNoSuchOrder()
    {
        _tmsApi.Answer(HttpStatusCode.NotFound, """{ "status": 404, "title": "Transport Order TO-0000043 not found." }""", "application/problem+json");

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Refused("There is no Transport Order TO-0000043."));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReRateAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var reRate = () => ReRateAsync("TO-0000043");

        await reRate.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AssignAsync_ToACarrier_PostsTheCarrierToTheOrdersAssign_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/transport-orders/TO-0000042/assign"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"Carrier","carrierId":"CAR-0002"}""");
    }

    [Fact]
    public async Task AssignAsync_ToOwnFleet_PostsOwnFleetWithoutACarrier()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", carrierId: null);

        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"OwnFleet"}""");
    }

    [Fact]
    public async Task AssignAsync_TwoAssignments_GoUnderKeysOfTheirOwn()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));
        await AssignAsync("TO-0000042", "CAR-0002");
        var first = _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single();

        await AssignAsync("TO-0000043", "CAR-0002");

        _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single().Should().NotBe(first);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)] // A retry the TMS answers as a repeat.
    public async Task AssignAsync_Assigned_IsPlannedWithItsShipmentNumber(HttpStatusCode status)
    {
        _tmsApi.Answer(status, Assigned("SH-0000012"));

        var answer = await AssignAsync("TO-0000042", "CAR-0002");

        answer.Should().Be(new AssignAnswer.Planned("SH-0000012"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "TransportOrderNotFound", "There is no Transport Order TO-0000042.")]
    [InlineData(HttpStatusCode.Conflict, "TransportOrderAlreadyPlanned", "Transport Order TO-0000042 is Planned already.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "InactiveCarrier", "Carrier CAR-0004 is not active.")]
    public async Task AssignAsync_Refused_IsRefusedWithTheTmsReason(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Transport Order can't be assigned.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await AssignAsync("TO-0000042", "CAR-0004");

        answer.Should().Be(new AssignAnswer.Refused(reason));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AssignAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var assign = () => AssignAsync("TO-0000042", "CAR-0002");

        await assign.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AssignAsync_Assigned_RecordsItsOutcomeOnTheAssignmentsTrail()
    {
        // The TMS starts the assignment's Message trail; the Planner's click joins it once the answer names it.
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-planning",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000042",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task AssignAsync_Refused_RecordsItRefused_WithTheTmsCode_OnNoTrail()
    {
        // The TMS's refusal names no correlation ID, so the Planner's click can't join the refusal's trail.
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """{ "status": 409, "title": "The Transport Order can't be assigned.", "detail": "Transport Order TO-0000042 is Planned already.", "code": "TransportOrderAlreadyPlanned" }""",
            "application/problem+json");

        await AssignAsync("TO-0000042", "CAR-0002");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "transport-planning",
            ["transportOrderNumber"] = "TO-0000042",
            ["outcome"] = "Refused",
            ["failureCode"] = "TransportOrderAlreadyPlanned",
        }).And.NotContainKey("correlationId");
    }

    [Fact]
    public async Task AssignAsync_SendsTheAssignmentInASpanOfItsOwn_WhichTheAnswersLogIsWrittenIn()
    {
        // The Integration console names the TMS UI's hop after it, not after Blazor's span for the click.
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        var span = _spans.Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("Assign Transport Order");
        _logger.LatestRecord.Timestamp.UtcDateTime.Should().BeOnOrAfter(span.StartTimeUtc).And.BeOnOrBefore(span.StartTimeUtc + span.Duration);
    }

    [Fact]
    public async Task ReRateAsync_Rated_RecordsItsOutcomeOnTheIntakesTrail()
    {
        // A re-rate continues the Message trail of the order's intake, as its first rating does.
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-0043",
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000043",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task ReRateAsync_FailedAgain_RecordsItSucceeded_WithTheNewFailureCode_AsTheTmsDoes()
    {
        // Rating failed is the rating's answer, not a failed flow.
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));

        await ReRateAsync("TO-0000043");

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Succeeded").And.Contain("failureCode", "WeightAboveHighestBreak");
    }

    [Fact]
    public async Task ReRateAsync_SendsTheReRateInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Re-rate Transport Order");
    }

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();

    private Task<AssignAnswer> AssignAsync(string number, string? carrierId) =>
        _client.AssignAsync(number, carrierId, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers an assignment: AssignTransportOrderResult, with its enums as strings.
    private static string Assigned(string shipmentNumber) =>
        $$"""
        {
          "shipmentNumber": "{{shipmentNumber}}",
          "transportOrderNumber": "TO-0000042",
          "status": "Planned",
          "carriedBy": { "assignedTo": "Carrier", "carrier": { "id": "CAR-0002", "name": "Kessler Spedition" } },
          "shipperId": "SHP-0001",
          "correlationId": "corr-planning",
          "repeat": false
        }
        """;

    private Task<ReRateAnswer> ReRateAsync(string number) => _client.ReRateAsync(number, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers a re-rate: RateTransportOrderResult, with its enums as strings.
    private static string Answer(string ratingResult, (string Code, string Message)? failure = null) =>
        $$"""
        {
          "ratingResult": "{{ratingResult}}",
          "ratingFailure": {{(failure is { } f ? $$"""{ "code": "{{f.Code}}", "message": "{{f.Message}}" }""" : "null")}},
          "shipperId": "SHP-0001",
          "correlationId": "corr-0043",
          "repeat": false
        }
        """;
}
