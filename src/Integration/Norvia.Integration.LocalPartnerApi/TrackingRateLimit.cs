namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Shipper product's rate limit per subscription (P8), played locally: the policies' <c>rate-limit-by-key</c> allows
/// <see cref="Calls"/> calls in <see cref="Window"/> for each subscription, shared by the four tracking operations. API
/// Management slides its window; this one is fixed, which is close enough for a Persona that polls every few seconds.
/// </summary>
internal sealed class TrackingRateLimit(TimeProvider time) : FixedWindowRateLimit(time, Calls, Window)
{
    public const int Calls = 120;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
