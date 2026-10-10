using System.Net;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>The Planner's actions on Draft Trips, as the TMS UI sends them to the TMS API (P11).</summary>
public sealed class TmsApiClientTripsTests : IDisposable
{
    private static readonly CancellationToken Cancel = Xunit.TestContext.Current.CancellationToken;

    private readonly StubHandler _tmsApi = new();
    private readonly ITmsApiTokens _tokens = Substitute.For<ITmsApiTokens>();
    private readonly TmsUiActivities _activities = new("Norvia.Tms.Web.Tests");
    private readonly HttpClient _http;
    private readonly TmsApiClient _client;

    public TmsApiClientTripsTests()
    {
        _http = new HttpClient(_tmsApi) { BaseAddress = new Uri("http://tms-api.example") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tms-ui-token");
        _client = new TmsApiClient(_http, _tokens, _activities, new FakeLogger<TmsApiClient>());
    }

    public void Dispose()
    {
        _activities.Dispose();
        _http.Dispose();
        _tmsApi.Dispose();
    }

    [Fact]
    public async Task CreateDraftTripAsync_PostsTheDayVehicleAndDriver_AndAnswersTheNumber()
    {
        _tmsApi.Answer(HttpStatusCode.Created, """{ "number": "TR-0000001" }""");

        var answer = await _client.CreateDraftTripAsync(new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", null), Cancel);

        answer.Should().Be(new TripAnswer.Created("TR-0000001"));
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.RequestBody.Should().Be("""{"day":"2026-10-14","vehicleId":"VEH-0001"}""");
    }

    [Fact]
    public async Task ChangeDraftTripAsync_PutsTheVehicleAndDriver()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        var answer = await _client.ChangeDraftTripAsync("TR-0000001", "VEH-0001", "DRV-0002", Cancel);

        answer.Should().Be(new TripAnswer.Done());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Put);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001"));
        _tmsApi.RequestBody.Should().Be("""{"vehicleId":"VEH-0001","driverId":"DRV-0002"}""");
    }

    [Fact]
    public async Task DiscardDraftTripAsync_DeletesTheTrip()
    {
        _tmsApi.Answer(HttpStatusCode.NoContent, string.Empty);

        (await _client.DiscardDraftTripAsync("TR-0000001", Cancel)).Should().Be(new TripAnswer.Done());

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Delete);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/trips/TR-0000001"));
        _tmsApi.RequestBody.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARefusal_AnswersTheProblemsDetail(HttpStatusCode status)
    {
        _tmsApi.Answer(status, """{ "code": "VehicleNotAvailable", "detail": "Vehicle VEH-0001 is not available on 2026-10-14: it has another Trip that day." }""", "application/problem+json");

        var answer = await _client.CreateDraftTripAsync(new TripEntry(new DateOnly(2026, 10, 14), "VEH-0001", null), Cancel);

        answer.Should().Be(new TripAnswer.Refused("Vehicle VEH-0001 is not available on 2026-10-14: it has another Trip that day."));
    }

    [Fact]
    public async Task ARefusalWithoutAProblem_SaysWhatWasRefused()
    {
        _tmsApi.Answer(HttpStatusCode.BadRequest, "{}");

        (await _client.DiscardDraftTripAsync("TR-0000001", Cancel)).Should().Be(new TripAnswer.Refused("Discard Draft Trip was refused."));
    }

    [Fact]
    public async Task AnUnexpectedStatus_Throws()
    {
        _tmsApi.Answer(HttpStatusCode.InternalServerError, string.Empty);

        var act = () => _client.DiscardDraftTripAsync("TR-0000001", Cancel);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
