using System.Collections.Concurrent;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>A fixed window rate limit per subscription, as the policies' <c>rate-limit-by-key</c> (ADR 0023). API Management slides its window; this one is fixed.</summary>
internal class FixedWindowRateLimit(TimeProvider time, int calls, TimeSpan window)
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();

    /// <summary>Counts a call for the subscription. Null when it is allowed; otherwise how long until the limit renews.</summary>
    public TimeSpan? Count(string subscriptionKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionKey);
        var now = time.GetUtcNow();
        var counter = _counters.GetOrAdd(subscriptionKey, _ => new Counter(now));
        lock (counter)
        {
            if (now - counter.WindowStart >= window)
            {
                counter.WindowStart = now;
                counter.Calls = 0;
            }

            if (counter.Calls >= calls)
            {
                return counter.WindowStart + window - now;
            }

            counter.Calls++;
            return null;
        }
    }

    private sealed class Counter(DateTimeOffset windowStart)
    {
        public DateTimeOffset WindowStart { get; set; } = windowStart;

        public int Calls { get; set; }
    }
}
