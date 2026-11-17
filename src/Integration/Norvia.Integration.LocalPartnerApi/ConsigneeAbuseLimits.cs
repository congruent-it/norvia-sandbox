using System.Collections.Concurrent;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Consignee operation's two limits per client IP (ADR 0052), played locally as the policy has them: 60 calls a minute, and a lower
/// limit of 10 answers of <c>TrackingCodeNotFound</c> a minute, so guessing is slow. API Management slides its window; these are fixed.
/// </summary>
internal sealed class ConsigneeAbuseLimits(TimeProvider time)
{
    public const int Calls = 60;
    public const int Misses = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly FixedWindowRateLimit _calls = new(time, Calls, Window);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> _misses = new();

    /// <summary>Counts a call for the client. Null when it is allowed; otherwise how long until the limit renews.</summary>
    public TimeSpan? CountCall(string clientIp) => _calls.Count(clientIp);

    /// <summary>Whether the client has used up its answers of TrackingCodeNotFound for this minute.</summary>
    public bool TooManyMisses(string clientIp) =>
        _misses.TryGetValue(clientIp, out var entry) && time.GetUtcNow() - entry.Start < Window && entry.Count >= Misses;

    public void CountMiss(string clientIp)
    {
        var now = time.GetUtcNow();
        _misses.AddOrUpdate(
            clientIp,
            _ => (now, 1),
            (_, entry) => now - entry.Start >= Window ? (now, 1) : (entry.Start, entry.Count + 1));
    }
}
