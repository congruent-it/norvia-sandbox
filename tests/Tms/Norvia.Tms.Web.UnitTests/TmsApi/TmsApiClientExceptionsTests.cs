using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.TmsApi;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>The Planner's actions on an Exception, as the TMS UI sends them to the TMS API (P7).</summary>
public sealed class TmsApiClientExceptionsTests : IDisposable
{
    private static readonly CancellationToken Cancel = Xunit.TestContext.Current.CancellationToken;

    private readonly StubHandler _tmsApi = new();
    private readonly ITmsApiTokens _tokens = Substitute.For<ITmsApiTokens>();
    private readonly FakeLogger<TmsApiClient> _logger = new();
    private readonly TmsUiActivities _activities = new("Norvia.Tms.Web.Tests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly TmsApiClient _client;

    public TmsApiClientExceptionsTests()
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
    public async Task NotifyShipperAsync_PostsTheMessageAndThePlanner_AsTheTmsUi_InASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        var answer = await _client.NotifyShipperAsync("EX-0000005", "Your delivery failed.", Cancel);

        answer.Should().Be(new ExceptionAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/exceptions/EX-0000005/notify-shipper"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        BodyShouldBe("""{ "message": "Your delivery failed.", "planner": "Anna de Vries" }""");
        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Notify Shipper");
    }

    [Fact]
    public async Task CancelCarrierBookingAsync_PostsTheReason_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        var answer = await _client.CancelCarrierBookingAsync("EX-0000005", "Won't make its collection", Cancel);

        answer.Should().Be(new ExceptionAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/exceptions/EX-0000005/cancel-carrier-booking"));
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        BodyShouldBe("""{ "reason": "Won't make its collection" }""");
    }

    [Fact]
    public async Task AgreeDeliveryWindowAsync_PostsTheWindow()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        var answer = await _client.AgreeDeliveryWindowAsync(
            "EX-0000005", new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero), Cancel);

        answer.Should().Be(new ExceptionAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/exceptions/EX-0000005/delivery-window"));
        BodyShouldBe("""{ "earliest": "2026-10-08T12:30:00+00:00", "latest": "2026-10-08T16:30:00+00:00" }""");
    }

    [Fact]
    public async Task AddNoteAsync_PostsTheNoteAndThePlanner()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        var answer = await _client.AddNoteAsync("EX-0000005", "Phoned Kessler", Cancel);

        answer.Should().Be(new ExceptionAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/exceptions/EX-0000005/notes"));
        BodyShouldBe("""{ "text": "Phoned Kessler", "planner": "Anna de Vries" }""");
    }

    [Fact]
    public async Task ResolveAsync_PostsTheOutcomeByName_AndTheRemark()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        var answer = await _client.ResolveAsync("EX-0000005", ExceptionOutcome.ClaimHandledOutsideTms, "Claim filed", Cancel);

        answer.Should().Be(new ExceptionAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/exceptions/EX-0000005/resolve"));
        BodyShouldBe("""{ "outcome": "ClaimHandledOutsideTms", "remark": "Claim filed" }""");
    }

    [Fact]
    public async Task AnAction_Done_IsLoggedOnTheMessageTrailTheAnswerNames()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Acted);

        await _client.AddNoteAsync("EX-0000005", "Phoned Kessler", Cancel);

        _logger.LatestRecord.Message.Should().Be("Note added on Exception EX-0000005");
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "exception-handling",
            ["outcome"] = "Succeeded",
            ["correlationId"] = "corr-act",
            ["shipmentNumber"] = "SH-0000012",
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ExceptionNotFound", "There is no Exception EX-0000005.")]
    [InlineData(HttpStatusCode.Conflict, "ExceptionNotOpen", "Exception EX-0000005 is Resolved, not Open.")]
    public async Task AnAction_Refused_IsRefusedWithTheTmsReason_AndLoggedWithItsFailureCode(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The note can't be added.", "detail": "{{reason}}", "code": "{{code}}", "correlationId": "corr-refused" }""",
            "application/problem+json");

        var answer = await _client.AddNoteAsync("EX-0000005", "Phoned Kessler", Cancel);

        answer.Should().Be(new ExceptionAnswer.Refused(reason));
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["outcome"] = "Refused",
            ["failureCode"] = code,
            ["correlationId"] = "corr-refused",
        });
    }

    [Fact]
    public async Task AnAction_AnInvalidRequest_IsRefusedWithTheErrorsTheTmsGives()
    {
        _tmsApi.Answer(
            HttpStatusCode.BadRequest,
            """{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "Reason": ["A reason is at most 500 characters."] } }""",
            "application/problem+json");

        var answer = await _client.CancelCarrierBookingAsync("EX-0000005", "x", Cancel);

        answer.Should().Be(new ExceptionAnswer.Refused("A reason is at most 500 characters."));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnAction_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var act = () => _client.ResolveAsync("EX-0000005", ExceptionOutcome.Redelivered, "ok", Cancel);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // As the TMS API answers an action on an Exception, as far as the TMS UI reads it.
    private const string Acted = """{ "exceptionNumber": "EX-0000005", "shipmentNumber": "SH-0000012", "correlationId": "corr-act" }""";

    private void BodyShouldBe(string json) =>
        JsonNode.DeepEquals(JsonNode.Parse(_tmsApi.RequestBody!), JsonNode.Parse(json)).Should().BeTrue($"the request body was {_tmsApi.RequestBody}");

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
}
