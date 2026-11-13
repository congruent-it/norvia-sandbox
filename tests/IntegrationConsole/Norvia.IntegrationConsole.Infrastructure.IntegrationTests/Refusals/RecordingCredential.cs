using Azure.Core;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>The console's identity, as a test sees it: hands out a token of its own and notes the scopes asked for.</summary>
internal sealed class RecordingCredential(string token) : TokenCredential
{
    public List<string[]> ScopesAsked { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        ScopesAsked.Add(requestContext.Scopes);
        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
