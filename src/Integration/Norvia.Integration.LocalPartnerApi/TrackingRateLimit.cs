using System.Collections.Concurrent;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Shipper product's rate limit per subscription (P8), played locally: the policies' <c>rate-limit-by-key</c> allows
/// <see cref="Calls"/> calls in <see cref="Window"/> for each subscription, shared by the four tracking operations. API
/// Management slides its window; this one is fixed, which is close enough for a Persona that polls every few seconds.
/// </summary>
internal sealed class TrackingRateLimit(TimeProvider time)
{
    public const int Calls = 120;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Counter> _counters = new();

    /// <summary>Counts a call for the subscription. Null when it is allowed; otherwise how long until the limit renews.</summary>
    public TimeSpan? Count(string subscriptionKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionKey);
        var now = time.GetUtcNow();
        var counter = _counters.GetOrAdd(subscriptionKey, _ => new Counter(now));
        lock (counter)
        {
            if (now - counter.WindowStart >= Window)
            {
                counter.WindowStart = now;
                counter.Calls = 0;
            }

            if (counter.Calls >= Calls)
            {
                return counter.WindowStart + Window - now;
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
