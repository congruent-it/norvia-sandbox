using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>Succeeds when the token's <c>azp</c> is <see cref="GatewayOptions.GatewayClientId"/>.</summary>
public sealed class GatewayHandler(IOptions<BookingDraftHostOptions> options) : AuthorizationHandler<GatewayRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, GatewayRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.User.FindFirstValue("azp"), options.Value.Api.GatewayClientId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
