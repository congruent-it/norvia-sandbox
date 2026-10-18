using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.TestSupport.Reset;

/// <summary>What the reset tests do at the TMS's boundary: reset it as the Simulator does, and read back what is left.</summary>
internal static class ResetCalls
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Resets the TMS as the given caller, once.</summary>
    public static Task<HttpResponseMessage> ResetOnceAsync(this HttpClient caller, CancellationToken cancellationToken) =>
        caller.PostAsync(new Uri("/v1/reset", UriKind.Relative), content: null, cancellationToken);

    /// <summary>
    /// Resets the TMS as the Simulator does, which must succeed, and returns what it deleted by kind. A subscription the fixture is still emptying makes the
    /// TMS refuse for a moment, as it does the Simulator, so the reset is tried again until the subscriptions are empty.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, int>> ResetAsync(this HttpClient simulator, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);
        while (true)
        {
            using var response = await simulator.ResetOnceAsync(timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.StatusCode == HttpStatusCode.Conflict && JsonNode.Parse(body)!["code"]?.GetValue<string>() == "SubscriptionBusy")
            {
                await Task.Delay(TmsFixture.PollInterval, timeout.Token);
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonNode.Parse(body)!["deleted"]!.AsArray().ToDictionary(kind => kind!["kind"]!.GetValue<string>(), kind => kind!["count"]!.GetValue<int>());
        }
    }

    /// <summary>The status the TMS answers when asked for the resource.</summary>
    public static async Task<HttpStatusCode> StatusOfAsync(this HttpClient caller, string path, CancellationToken cancellationToken)
    {
        using var response = await caller.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        return response.StatusCode;
    }

    public static async Task<JsonArray> ListAsync(this HttpClient caller, string path, CancellationToken cancellationToken)
    {
        using var response = await caller.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>The Planner drafts a Trip with no Vehicle or Driver for the day.</summary>
    public static async Task<string> DraftTripAsync(this HttpClient planner, DateOnly day, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/trips", UriKind.Relative))
        {
            Content = JsonContent.Create(new { day = $"{day:yyyy-MM-dd}", vehicleId = (string?)null, driverId = (string?)null }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await planner.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["number"]!.GetValue<string>();
    }
}
