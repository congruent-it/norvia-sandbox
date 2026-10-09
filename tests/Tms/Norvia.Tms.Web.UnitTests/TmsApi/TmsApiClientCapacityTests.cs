using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Web.Capacity;
using Norvia.Tms.Web.TmsApi;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>The Planner's edits of the Own fleet's availability, as the TMS UI sends them to the TMS API (P10).</summary>
public sealed class TmsApiClientCapacityTests : IDisposable
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

    public TmsApiClientCapacityTests()
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
    public async Task RecordAbsenceAsync_PostsThePeriod_AsTheTmsUi_InASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, """{ "from": "2026-10-14", "to": "2026-10-15", "days": 2 }""");

        var answer = await _client.RecordAbsenceAsync(new PeriodEntry("DRV-0001", new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15), "Leave", "Holiday"), Cancel);

        answer.Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/capacity/absences"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        BodyShouldBe("""{ "id": "DRV-0001", "from": "2026-10-14", "to": "2026-10-15", "reason": "Leave", "remark": "Holiday" }""");
        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Record Absence");
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Message == "Record Absence recorded");
    }

    [Fact]
    public async Task RecordOffTheRoadAsync_PostsThePeriod()
    {
        _tmsApi.Answer(HttpStatusCode.OK, """{ "days": 1 }""");

        var answer = await _client.RecordOffTheRoadAsync(new PeriodEntry("VEH-0003", new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), "Repair", string.Empty), Cancel);

        answer.Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/capacity/off-the-road-days"));
        BodyShouldBe("""{ "id": "VEH-0003", "from": "2026-10-14", "to": "2026-10-14", "reason": "Repair", "remark": "" }""");
    }

    [Fact]
    public async Task RemoveAbsenceAsync_AndRemoveOffTheRoadAsync_DeleteTheRecord()
    {
        var id = Guid.Parse("0197a2c4-0000-7000-8000-000000000001");
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        (await _client.RemoveAbsenceAsync(id, Cancel)).Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Delete);
        _tmsApi.Request.RequestUri.Should().Be(new Uri($"http://tms-api.example/v1/capacity/absences/{id}"));
        _tmsApi.RequestBody.Should().BeNull();

        (await _client.RemoveOffTheRoadAsync(id, Cancel)).Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request.RequestUri.Should().Be(new Uri($"http://tms-api.example/v1/capacity/off-the-road-days/{id}"));
    }

    [Fact]
    public async Task ChangeRosterAsync_PutsTheWeekdaysByName()
    {
        _tmsApi.Answer(HttpStatusCode.OK, """{ "daysRewritten": 9 }""");

        var answer = await _client.ChangeRosterAsync(
            "DRV-0001",
            [new RosterWeekdayView(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(14, 30)), new RosterWeekdayView(DayOfWeek.Friday, new TimeOnly(7, 0), new TimeOnly(15, 0))],
            Cancel);

        answer.Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Put);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/capacity/rosters/DRV-0001"));
        BodyShouldBe("""{ "weekdays": { "Monday": { "start": "06:00:00", "end": "14:30:00" }, "Friday": { "start": "07:00:00", "end": "15:00:00" } } }""");
    }

    [Fact]
    public async Task EditShiftAsync_PutsTheShiftOnTheDay()
    {
        _tmsApi.Answer(HttpStatusCode.OK, """{ "date": "2026-10-14" }""");

        var answer = await _client.EditShiftAsync(new DateOnly(2026, 10, 14), "DRV-0001", new TimeOnly(5, 0), new TimeOnly(13, 0), Cancel);

        answer.Should().Be(new CapacityAnswer.Done());
        _tmsApi.Request!.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/capacity/days/2026-10-14/shifts/DRV-0001"));
        BodyShouldBe("""{ "start": "05:00:00", "end": "13:00:00" }""");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ARefusedEdit_IsAnswerAsRefused_WithTheProblemsDetail_AndLoggedWithItsCode(HttpStatusCode status)
    {
        _tmsApi.Answer(status, """{ "detail": "2026-10-10 is closed: days before today are read-only.", "code": "DayClosed" }""", "application/problem+json");

        var answer = await _client.EditShiftAsync(new DateOnly(2026, 10, 10), "DRV-0001", new TimeOnly(5, 0), new TimeOnly(13, 0), Cancel);

        answer.Should().Be(new CapacityAnswer.Refused("2026-10-10 is closed: days before today are read-only."));
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Message.Contains("refused with DayClosed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnInvalidRequest_IsAnswerAsRefused_WithItsValidationMessages()
    {
        _tmsApi.Answer(HttpStatusCode.BadRequest, """{ "errors": { "Reason": ["A reason is Leave, Sickness or Training."] } }""", "application/problem+json");

        var answer = await _client.RecordAbsenceAsync(new PeriodEntry("DRV-0001", new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), "Holiday", string.Empty), Cancel);

        answer.Should().Be(new CapacityAnswer.Refused("A reason is Leave, Sickness or Training."));
    }

    [Fact]
    public async Task ARefusalWithoutABody_IsAnswerAsRefused_WithAGenericReason()
    {
        _tmsApi.Answer(HttpStatusCode.Conflict, "{}", "application/problem+json");

        var answer = await _client.RemoveAbsenceAsync(Guid.NewGuid(), Cancel);

        answer.Should().Be(new CapacityAnswer.Refused("Remove Absence was refused."));
    }

    [Fact]
    public async Task AServerError_IsNotAnAnswer_ItThrows()
    {
        _tmsApi.Answer(HttpStatusCode.InternalServerError, "{}");

        var act = () => _client.RemoveAbsenceAsync(Guid.NewGuid(), Cancel);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private void BodyShouldBe(string json) =>
        JsonNode.DeepEquals(JsonNode.Parse(_tmsApi.RequestBody!), JsonNode.Parse(json)).Should().BeTrue($"the request body was {_tmsApi.RequestBody}");
}
