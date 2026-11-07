using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Norvia.Erp.Infrastructure.Agents;

/// <summary>Calls the MCP server with the Agent's token, as Foundry does with the Agent's identity.</summary>
internal sealed class BearerTokenHandler(IOptions<AgentClientOptions> options, TimeProvider time) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A caller that is another Agent's identity (the rejection reason agent) sets its own token first.
        request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", AgentToken.Create(options.Value.Tokens, time));
        return base.SendAsync(request, cancellationToken);
    }
}
