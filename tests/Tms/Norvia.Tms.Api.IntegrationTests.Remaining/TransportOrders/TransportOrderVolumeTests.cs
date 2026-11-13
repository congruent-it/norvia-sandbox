using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// The Estate health check reads how many Transport Orders came in each day to set the estate's bill against (#504). It does so as its own
/// identity with a role of its own, which opens that count and no Transport Order; the TMS UI's manage role does not open it.
/// </summary>
public sealed class TransportOrderVolumeTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Volume_AfterATransportOrderIsAccepted_CountsItOnTodaysDate()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);

        using var response = await tms.HealthCheck.GetAsync(new Uri($"/v1/transport-orders/volume?from={today.AddDays(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var days = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!.AsArray();
        days.Single(day => day!["date"]!.GetValue<string>() == $"{today:yyyy-MM-dd}")!["count"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Volume_WithAWindowThatEndsBeforeItStarts_IsABadRequest()
    {
        using var response = await tms.HealthCheck.GetAsync(new Uri("/v1/transport-orders/volume?from=2026-11-10&to=2026-11-09", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Volume_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await caller.GetAsync(new Uri("/v1/transport-orders/volume?from=2026-11-10&to=2026-11-10", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("intake")]
    [InlineData("manage")]
    public async Task Volume_WithAnotherRole_IsForbidden(string holder)
    {
        var caller = holder == "intake" ? tms.Tms : tms.TmsUi;

        using var response = await caller.GetAsync(new Uri("/v1/transport-orders/volume?from=2026-11-10&to=2026-11-10", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
