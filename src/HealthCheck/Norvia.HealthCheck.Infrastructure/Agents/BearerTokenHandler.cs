using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Norvia.HealthCheck.Infrastructure.Agents;

/// <summary>Calls the MCP server with the Agent's token, as Foundry does with the Agent's identity.</summary>
internal sealed class BearerTokenHandler(IOptions<AgentClientOptions> options, TimeProvider time) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken.Create(options.Value.TokensFor(request.RequestUri), time));
        return base.SendAsync(request, cancellationToken);
    }
}
