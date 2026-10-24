using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>
/// The policy's <c>rate-limit-by-key</c> on the token's <c>azp</c>, played locally: each Agent identity has its own budget.
/// API Management slides its window; this one is fixed, which is close enough for an Agent that asks a few questions a minute.
/// </summary>
internal sealed class AgentRateLimit(TimeProvider time, IOptions<AgentMcpOptions> options)
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();

    /// <summary>Counts a call for the Agent. Null when it is allowed; otherwise how long until the limit renews.</summary>
    public TimeSpan? Count(string agentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        var window = TimeSpan.FromSeconds(options.Value.RateLimitPeriodSeconds);
        var now = time.GetUtcNow();
        var counter = _counters.GetOrAdd(agentId, _ => new Counter(now));
        lock (counter)
        {
            if (now - counter.WindowStart >= window)
            {
                counter.WindowStart = now;
                counter.Calls = 0;
            }

            if (counter.Calls >= options.Value.RateLimitCalls)
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
