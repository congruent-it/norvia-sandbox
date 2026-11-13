using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>
/// Locally the console lists the refusals of the stand-in that plays API Management from the Aspire dashboard, read through its telemetry API as
/// the AppHost configures it (ADR 0027), under Aspire's test host.
/// </summary>
public sealed class AspireDashboardRefusedCallsTests(LocalTelemetryFixture local) : IClassFixture<LocalTelemetryFixture>
{
    // The stand-in's Shipper tracking limit per subscription and minute.
    private const int TrackingCallsPerMinute = 120;

    private static readonly Uri SubmitTransportOrder = new("/partner/v1/transport-orders", UriKind.Relative);

    [Fact]
    public async Task RefusalsAsync_ListsTheRefusalsTheStandInAnsweredWith_ByStatusAndFailureCode()
    {
        var before = local.ReadyAt;
        var noKey = await PostAsync(subscriptionKey: null, token: null);
        var noToken = await PostAsync(LocalTelemetryFixture.SubscriptionKey, token: null);
        var noRole = await PostAsync(LocalTelemetryFixture.SubscriptionKey, local.Token(LocalTelemetryFixture.ShipperClientId, "Tracking.Read"));
        var noShipper = await PostAsync(LocalTelemetryFixture.SubscriptionKey, local.Token(LocalTelemetryFixture.UnregisteredClientId, "TransportOrders.Submit"));
        noKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        noToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        noRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noShipper.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var refusals = await EventuallyAsync(before, found => found.Count >= 4);

        refusals.Select(refusal => (refusal.StatusCode, refusal.FailureCode)).Should().BeEquivalentTo(
            [(401, "Unauthorized"), (401, "Unauthorized"), (403, "NotAllowedToSubmit"), (403, "NoShipperForClient")]);
        // Each opens its Message trail by the correlation ID API Management answered with, and no caller's subscription is known locally.
        refusals.Should().OnlyContain(refusal => refusal.CorrelationId != null && refusal.Caller == null);
        refusals.Select(refusal => refusal.CorrelationId).Should().BeEquivalentTo(
            new[] { noKey, noToken, noRole, noShipper }.Select(response => response.Headers.GetValues("X-Correlation-ID").Single()));
    }

    [Fact]
    public async Task RefusalsAsync_ListsAShipperOverItsRateLimit_As429()
    {
        var before = local.ReadyAt;
        var token = local.Token(LocalTelemetryFixture.ShipperClientId, "Tracking.Read");
        HttpResponseMessage? last = null;
        for (var call = 0; call <= TrackingCallsPerMinute; call++)
        {
            last?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/partner/v1/transport-orders", UriKind.Relative));
            request.Headers.Add("Ocp-Apim-Subscription-Key", LocalTelemetryFixture.SubscriptionKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            last = await local.StandIn.SendAsync(request, TestContext.Current.CancellationToken);
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var refusals = await EventuallyAsync(before, found => found.Any(refusal => refusal.StatusCode == 429));

        refusals.Should().ContainSingle(refusal => refusal.StatusCode == 429).Which.FailureCode.Should().Be("RateLimitExceeded");
    }

    private async Task<HttpResponseMessage> PostAsync(string? subscriptionKey, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SubmitTransportOrder) { Content = new StringContent("{}") };
        if (subscriptionKey is not null)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await local.StandIn.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The dashboard takes a moment to receive what the stand-in exported.
    private async Task<IReadOnlyList<RecordedRefusal>> EventuallyAsync(DateTimeOffset since, Func<IReadOnlyList<RecordedRefusal>, bool> enough)
    {
        using var http = new HttpClient { BaseAddress = local.DashboardUrl };
        http.DefaultRequestHeaders.Add(DashboardTrailSource.ApiKeyHeader, LocalTelemetryFixture.ApiKey);
        var source = new DashboardTrailSource(http, Options.Create(new DashboardTrailSourceOptions { DashboardUrl = local.DashboardUrl, ApiKey = LocalTelemetryFixture.ApiKey }));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            IReadOnlyList<RecordedRefusal> found;
            try
            {
                found = await source.RefusalsAsync(since, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                var raw = await http.GetStringAsync(new Uri("api/telemetry/logs?limit=50", UriKind.Relative), TestContext.Current.CancellationToken);
                throw new InvalidOperationException(raw[..Math.Min(raw.Length, 3000)]);
            }

            if (enough(found) || timeout.IsCancellationRequested)
            {
                return found;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                var raw = await http.GetStringAsync(new Uri("api/telemetry/logs?limit=50", UriKind.Relative), TestContext.Current.CancellationToken);
                throw new InvalidOperationException(found.Count + " refusals; " + raw[..Math.Min(raw.Length, 3000)]);
            }
        }
    }
}
