using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Erp.Mcp.IntegrationTests.FinanceAgent;

/// <summary>
/// The local MCP stand-in (ADR 0041) running in memory, configured as the AppHost configures it for the Carrier invoice matching agent. It holds the key the
/// stand-in trusts, so it issues the Agent's token as Entra would. Each test builds one of its own.
/// </summary>
internal sealed class StandInHost : WebApplicationFactory<Program>
{
    public const string Issuer = "https://norvia.local/tokens";
    public const string FinanceAudience = "api://norvia-finance-matching-agent";
    public const string FinanceRole = "CarrierInvoices.FinanceAgent.Read";
    public const string FinanceAgentId = "norvia-local-agent-finance-matching";

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(64);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var key = Convert.ToBase64String(_signingKey);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            // The stand-in's first server (the Integration console's) and the Customer service reply's are not optional; the Agent under test's are.
            ["AgentMcp:Tokens:Issuer"] = Issuer,
            ["AgentMcp:Tokens:Audience"] = "api://norvia-integration-console-agent",
            ["AgentMcp:Tokens:SigningKey"] = key,
            ["AgentMcp:ConsoleAgentApi"] = "http://integration-console/agent/v1",
            ["AgentMcp:ConsoleTokens:Issuer"] = Issuer,
            ["AgentMcp:ConsoleTokens:Audience"] = "api://norvia-integration-console-agent-api",
            ["AgentMcp:ConsoleTokens:SigningKey"] = key,
            ["AgentMcp:CustomerServiceReply:Tokens:Issuer"] = Issuer,
            ["AgentMcp:CustomerServiceReply:Tokens:Audience"] = "api://norvia-customer-service-reply-agent",
            ["AgentMcp:CustomerServiceReply:Tokens:SigningKey"] = key,
            ["AgentMcp:CustomerServiceReply:Api"] = "http://tms-api/v1/agent/customer-service-reply",
            ["AgentMcp:CustomerServiceReply:ApiTokens:Issuer"] = Issuer,
            ["AgentMcp:CustomerServiceReply:ApiTokens:Audience"] = "api://norvia-tms",
            ["AgentMcp:CustomerServiceReply:ApiTokens:SigningKey"] = key,
            ["AgentMcp:FinanceMatching:Tokens:Issuer"] = Issuer,
            ["AgentMcp:FinanceMatching:Tokens:Audience"] = FinanceAudience,
            ["AgentMcp:FinanceMatching:Tokens:SigningKey"] = key,
            ["AgentMcp:FinanceMatching:Api"] = "http://erp-api/v1/agent/finance",
            ["AgentMcp:FinanceMatching:ApiTokens:Issuer"] = Issuer,
            ["AgentMcp:FinanceMatching:ApiTokens:Audience"] = "api://norvia-erp",
            ["AgentMcp:FinanceMatching:ApiTokens:SigningKey"] = key,
        })
        {
            builder.UseSetting(name, value);
        }
    }

    /// <summary>The Agent's token as Entra issues it to its agent identity.</summary>
    private string AgentToken()
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = FinanceAudience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = FinanceAgentId, ["roles"] = new[] { FinanceRole } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>The tools the stand-in's Carrier invoice matching server lists, as an MCP client sees them.</summary>
    public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken());
        using var content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/mcp/finance-matching", UriKind.Relative), content, cancellationToken);

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["result"]!["tools"]!.AsArray();
    }
}
