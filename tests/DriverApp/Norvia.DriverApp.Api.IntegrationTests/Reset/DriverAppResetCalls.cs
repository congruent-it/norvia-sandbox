using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Api.IntegrationTests.Reset;

/// <summary>What the Driver app's reset tests do at its boundary: send it a Trip, and reset it as the Simulator does.</summary>
internal static class DriverAppResetCalls
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>A Trip for the Driver today, sent to the Driver app as the Trip dispatch Logic App does.</summary>
    public static async Task ReceiveTripAsync(this DriverAppFixture driverApp, string trip, string driverId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")).DateTime);
        using var response = await driverApp.DriverApp.PostAsync(
            "/v1/trips",
            new StringContent(await DriverAppFixture.SampleAsync("two-shipments", trip, version: 1, driverId, sample => sample["day"] = $"{today:yyyy-MM-dd}"), Encoding.UTF8, "application/json"),
            cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Resets the Driver app as the Simulator does, which must succeed, and returns what it deleted by kind. The Driver app refuses for a moment while the
    /// fixture is still emptying its subscription, as it does the Simulator, so the reset is tried again until the subscription is empty.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, int>> ResetAsync(this HttpClient simulator, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);
        while (true)
        {
            using var response = await simulator.PostAsync("/v1/reset", content: null, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.StatusCode == HttpStatusCode.Conflict && JsonNode.Parse(body)!["code"]?.GetValue<string>() == "SubscriptionBusy")
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonNode.Parse(body)!["deleted"]!.AsArray().ToDictionary(kind => kind!["kind"]!.GetValue<string>(), kind => kind!["count"]!.GetValue<int>());
        }
    }
}
