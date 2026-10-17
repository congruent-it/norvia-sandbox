using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;

namespace Norvia.Tms.TestSupport;

/// <summary>What the rating tests do at the TMS's boundary: submit as an intake caller, read the query API, watch the topic.</summary>
public static class TmsCalls
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    public static string NewCorrelationId() => Guid.NewGuid().ToString();

    /// <summary>Submits a booking the TMS must accept, as the given intake caller or the intake Logic App, and returns its number.</summary>
    public static async Task<string> SubmitAcceptedAsync(
        this TmsFixture tms, JsonObject booking, string correlationId, CancellationToken cancellationToken, HttpClient? caller = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await (caller ?? tms.Tms).SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["transportOrderNumber"]!.GetValue<string>();
    }

    public static async Task<JsonNode> GetTransportOrderAsync(this TmsFixture tms, string number, CancellationToken cancellationToken)
    {
        using var response = await tms.Tms.GetAsync(new Uri($"/v1/transport-orders/{number}", UriKind.Relative), cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
    }

    /// <summary>
    /// Re-rates the Transport Order as Customer service does, through the TMS UI, under the given idempotency key or a new one.
    /// </summary>
    public static async Task<HttpResponseMessage> ReRateAsync(
        this TmsFixture tms, string number, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/transport-orders/{number}/re-rate", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await tms.TmsUi.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonArray> ListRatingExceptionsAsync(this TmsFixture tms, CancellationToken cancellationToken)
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/transport-orders/rating-exceptions", UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>
    /// Waits until the given number of events of the type carrying the correlation ID have been published, then lets the
    /// outbox run a few more cycles so one more would show up too.
    /// </summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PublishedAsync(
        this TmsFixture tms, string correlationId, string eventType, int count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while (tms.Published.Count(m => m.CorrelationId == correlationId && m.Subject == eventType) < count)
        {
            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }

        await TmsFixture.SettleOutboxAsync(cancellationToken);
        return [.. tms.Published.Where(m => m.CorrelationId == correlationId && m.Subject == eventType)];
    }

    /// <summary>Waits until the Transport Order is rated, or its rating failed, whichever it is.</summary>
    public static async Task WaitForRatingToSettleAsync(this TmsFixture tms, string number, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while ((await tms.GetTransportOrderAsync(number, cancellationToken))["ratingResult"]?.GetValue<string>() is not ("Rated" or "RatingFailed"))
        {
            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }
    }

    /// <summary>Reads the Transport Order until it has the rating result, as Rating runs a moment after acceptance.</summary>
    public static async Task<JsonNode> WaitForRatingResultAsync(this TmsFixture tms, string number, string ratingResult, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while (true)
        {
            var order = await tms.GetTransportOrderAsync(number, cancellationToken);
            if (order["ratingResult"]?.GetValue<string>() == ratingResult)
            {
                return order;
            }

            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }
    }

    /// <summary>
    /// Waits for the first event of the type carrying the correlation ID, then lets the outbox run a few more cycles so a
    /// duplicate would show up too.
    /// </summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PublishedAsync(
        this TmsFixture tms, string correlationId, string eventType, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        while (!tms.Published.Any(m => m.CorrelationId == correlationId && m.Subject == eventType))
        {
            await Task.Delay(TmsFixture.PollInterval, timeout.Token);
        }

        await TmsFixture.SettleOutboxAsync(cancellationToken);
        return [.. tms.Published.Where(m => m.CorrelationId == correlationId && m.Subject == eventType)];
    }
}
