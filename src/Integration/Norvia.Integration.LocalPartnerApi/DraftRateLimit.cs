using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The Booking draft operation's rate limit per subscription, played locally: stricter than the booking's, and its own counter.</summary>
internal sealed class DraftRateLimit(TimeProvider time, IOptions<AgentsOptions> options) : FixedWindowRateLimit(time, options.Value.DraftsPerMinute, TimeSpan.FromMinutes(1));
