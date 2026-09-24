using System.Security.Claims;
using Microsoft.Extensions.Options;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>Finds the intake channel a caller submits for, from the client ID in its token.</summary>
public sealed class IntakeCallers(IOptions<IntakeCallersOptions> options)
{
    // Client IDs are GUIDs in Azure, which tokens and configuration may write in different case.
    private readonly Dictionary<string, IntakeChannel> _channelByClientId =
        new(options.Value.ChannelByClientId, StringComparer.OrdinalIgnoreCase);

    public bool TryFindChannel(ClaimsPrincipal caller, out IntakeChannel channel)
    {
        ArgumentNullException.ThrowIfNull(caller);
        channel = default;
        var clientId = caller.FindFirst("azp")?.Value ?? caller.FindFirst("appid")?.Value;
        return clientId is not null && _channelByClientId.TryGetValue(clientId, out channel);
    }
}
