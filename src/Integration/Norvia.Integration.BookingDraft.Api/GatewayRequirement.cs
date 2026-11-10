using Microsoft.AspNetCore.Authorization;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>The token was issued to the gateway's client.</summary>
public sealed class GatewayRequirement : IAuthorizationRequirement;
