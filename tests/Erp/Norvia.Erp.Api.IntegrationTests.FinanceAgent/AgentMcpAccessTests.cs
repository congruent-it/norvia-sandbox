using System.Net;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// The MCP stand-in makes the checks of API Management's policy, in its order: the Agent's token (401), its read role (403) and the rate limit (429).
/// Each test calls with an Agent identity of its own, as the rate limit counts the calls of one.
/// </summary>
public sealed class AgentMcpAccessTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACallWithNoToken_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(token: null);

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithATokenTheStandInDoesNotTrust_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken(signingKey: Convert.ToHexString(Guid.NewGuid().ToByteArray())));

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithATokenForAnotherAudience_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken(audience: Norvia.AppHost.LocalTokens.ErpAudience));

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithAnExpiredToken_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken(lifetime: TimeSpan.FromHours(-1)));

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallOfTheTool_WithNoToken_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(token: null);

        using var response = await client.CallToolRawAsync(_cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallOfTheTool_WithATokenWithoutTheReadRole_IsRefused403()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken(roles: ["Something.Else"]));

        using var response = await client.CallToolRawAsync(_cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallOfTheTool_OverTheRateLimit_IsRefused429()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());
        await UseUpTheLimitAsync(client);

        using var response = await client.CallToolRawAsync(_cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ACallWithATokenWithoutTheReadRole_IsRefused403()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken(roles: ["Something.Else"]));

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallWithTheReadRole_ListsTheOneTool()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(_cancellationToken)).Should().Contain(FinanceAgentCalls.Tool);
    }

    [Fact]
    public async Task ACallOverTheRateLimit_IsRefused429WithRetryAfter()
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());
        await UseUpTheLimitAsync(client);

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task TheChecksAreMadeInOrder_ARateLimitedAgentIsStillAskedForItsTokenAndItsRoleFirst()
    {
        var agentId = FinanceAgentFixture.NewAgentId();
        using var agent = fixture.CreateMcpClient(fixture.AgentToken(agentId));
        await UseUpTheLimitAsync(agent);
        using var noToken = fixture.CreateMcpClient(token: null);
        using var noRole = fixture.CreateMcpClient(fixture.AgentToken(agentId, roles: []));

        using var first = await noToken.CallAsync("tools/list", _cancellationToken);
        using var second = await noRole.CallAsync("tools/list", _cancellationToken);
        using var third = await agent.CallAsync("tools/list", _cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task UseUpTheLimitAsync(HttpClient client)
    {
        for (var call = 0; call < FinanceAgentFixture.RateLimitCalls; call++)
        {
            using var response = await client.CallAsync("tools/list", _cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"call {call + 1} is within the limit");
        }
    }
}
