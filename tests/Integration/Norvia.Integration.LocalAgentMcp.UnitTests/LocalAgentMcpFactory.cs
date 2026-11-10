using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// Runs the MCP stand-in in memory, configured as the AppHost configures it, with the console's agent API replaced by
/// <see cref="Console"/>. It holds the key the stand-in trusts, so it issues Agent tokens as Entra would.
/// </summary>
public sealed class LocalAgentMcpFactory : WebApplicationFactory<Program>
{
    public const string AgentId = "norvia-local-agent-integration-operations";
    public const string Role = "IntegrationConsole.Agent.Read";
    public const string Issuer = "https://norvia.local/tokens";
    public const string AgentAudience = "api://norvia-integration-console-agent";
    public const string ConsoleAudience = "api://norvia-integration-console-agent-api";
    public const string HealthCheckAgentId = "norvia-local-agent-estate-health-check";
    public const string HealthCheckRole = "HealthCheck.Agent.Read";
    public const string HealthCheckAudience = "api://norvia-health-check-agent";
    public const string HealthCheckApiAudience = "api://norvia-health-check-agent-api";

    public const string CustomerServiceAgentId = "norvia-local-agent-customer-service";
    public const string CustomerServiceRole = "TransportOrders.CustomerServiceAgent.Read";
    public const string CustomerServiceAudience = "api://norvia-customer-service-agent";
    public const string CustomerServiceReplyAgentId = "norvia-local-agent-customer-service-reply";
    public const string CustomerServiceReplyRole = "TransportOrders.CustomerServiceReplyAgent.Read";
    public const string CustomerServiceReplyAudience = "api://norvia-customer-service-reply-agent";
    public const string PlannerAgentId = "norvia-local-agent-planner";
    public const string PlannerRole = "TransportOrders.PlannerAgent.Read";
    public const string PlannerAudience = "api://norvia-planner-agent";
    public const string TmsAudience = "api://norvia-tms";
    public const string FinanceAgentId = "norvia-local-agent-finance-matching";
    public const string FinanceRole = "CarrierInvoices.FinanceAgent.Read";
    public const string FinanceAudience = "api://norvia-finance-matching-agent";
    public const string FinanceRejectionReasonAgentId = "norvia-local-agent-finance-rejection-reason";
    public const string FinanceRejectionReasonRole = "CarrierInvoices.FinanceRejectionReasonAgent.Read";
    public const string FinanceRejectionReasonAudience = "api://norvia-finance-rejection-reason-agent";
    public const string ErpAudience = "api://norvia-erp";

    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(64);

    public StubConsole Console { get; } = new();

    internal ManualTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public int RateLimitCalls { get; set; } = 60;

    /// <summary>Whether the stand-in is configured to serve the Estate health check's server, as the AppHost configures it.</summary>
    public bool ServesHealthCheck { get; set; } = true;

    /// <summary>Whether the stand-in is configured to serve the Customer service agent's server, as the AppHost configures it.</summary>
    public bool ServesCustomerService { get; set; } = true;

    /// <summary>Whether the stand-in is configured to serve the Carrier invoice matching agent's server, as the AppHost configures it.</summary>
    public bool ServesFinanceMatching { get; set; } = true;

    public FakeLogCollector Logs => Services.GetRequiredService<FakeLogCollector>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var signingKey = Convert.ToBase64String(SigningKey);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["AgentMcp:Tokens:Issuer"] = Issuer,
            ["AgentMcp:Tokens:Audience"] = AgentAudience,
            ["AgentMcp:Tokens:SigningKey"] = signingKey,
            ["AgentMcp:ConsoleAgentApi"] = "http://integration-console/agent/v1",
            ["AgentMcp:ConsoleTokens:Issuer"] = Issuer,
            ["AgentMcp:ConsoleTokens:Audience"] = ConsoleAudience,
            ["AgentMcp:ConsoleTokens:SigningKey"] = signingKey,
            ["AgentMcp:RateLimitCalls"] = RateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        {
            builder.UseSetting(name, value);
        }

        if (ServesHealthCheck)
        {
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["AgentMcp:HealthCheck:Tokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheck:Tokens:Audience"] = HealthCheckAudience,
                ["AgentMcp:HealthCheck:Tokens:SigningKey"] = signingKey,
                ["AgentMcp:HealthCheck:Api"] = "http://health-check/agent/v1",
                ["AgentMcp:HealthCheck:ApiTokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheck:ApiTokens:Audience"] = HealthCheckApiAudience,
                ["AgentMcp:HealthCheck:ApiTokens:SigningKey"] = signingKey,
            })
            {
                builder.UseSetting(name, value);
            }
        }

        if (ServesCustomerService)
        {
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["AgentMcp:CustomerService:Tokens:Issuer"] = Issuer,
                ["AgentMcp:CustomerService:Tokens:Audience"] = CustomerServiceAudience,
                ["AgentMcp:CustomerService:Tokens:SigningKey"] = signingKey,
                ["AgentMcp:CustomerService:Api"] = "http://tms-api/v1/agent/customer-service",
                ["AgentMcp:CustomerService:ApiTokens:Issuer"] = Issuer,
                ["AgentMcp:CustomerService:ApiTokens:Audience"] = TmsAudience,
                ["AgentMcp:CustomerService:ApiTokens:SigningKey"] = signingKey,
            })
            {
                builder.UseSetting(name, value);
            }
        }

        if (ServesFinanceMatching)
        {
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["AgentMcp:FinanceMatching:Tokens:Issuer"] = Issuer,
                ["AgentMcp:FinanceMatching:Tokens:Audience"] = FinanceAudience,
                ["AgentMcp:FinanceMatching:Tokens:SigningKey"] = signingKey,
                ["AgentMcp:FinanceMatching:Api"] = "http://erp-api/v1/agent/finance",
                ["AgentMcp:FinanceMatching:ApiTokens:Issuer"] = Issuer,
                ["AgentMcp:FinanceMatching:ApiTokens:Audience"] = ErpAudience,
                ["AgentMcp:FinanceMatching:ApiTokens:SigningKey"] = signingKey,
            })
            {
                builder.UseSetting(name, value);
            }
        }

        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["AgentMcp:CustomerServiceReply:Tokens:Issuer"] = Issuer,
            ["AgentMcp:CustomerServiceReply:Tokens:Audience"] = CustomerServiceReplyAudience,
            ["AgentMcp:CustomerServiceReply:Tokens:SigningKey"] = signingKey,
            ["AgentMcp:CustomerServiceReply:Api"] = "http://tms-api/v1/agent/customer-service-reply",
            ["AgentMcp:CustomerServiceReply:ApiTokens:Issuer"] = Issuer,
            ["AgentMcp:CustomerServiceReply:ApiTokens:Audience"] = TmsAudience,
            ["AgentMcp:CustomerServiceReply:ApiTokens:SigningKey"] = signingKey,
            ["AgentMcp:Planner:Tokens:Issuer"] = Issuer,
            ["AgentMcp:Planner:Tokens:Audience"] = PlannerAudience,
            ["AgentMcp:Planner:Tokens:SigningKey"] = signingKey,
            ["AgentMcp:Planner:Api"] = "http://tms-api/v1/agent/planner",
            ["AgentMcp:Planner:ApiTokens:Issuer"] = Issuer,
            ["AgentMcp:Planner:ApiTokens:Audience"] = TmsAudience,
            ["AgentMcp:Planner:ApiTokens:SigningKey"] = signingKey,
        })
        {
            builder.UseSetting(name, value);
        }

        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["AgentMcp:FinanceRejectionReason:Tokens:Issuer"] = Issuer,
            ["AgentMcp:FinanceRejectionReason:Tokens:Audience"] = FinanceRejectionReasonAudience,
            ["AgentMcp:FinanceRejectionReason:Tokens:SigningKey"] = signingKey,
            ["AgentMcp:FinanceRejectionReason:Api"] = "http://erp-api/v1/agent/finance-rejection-reason",
            ["AgentMcp:FinanceRejectionReason:ApiTokens:Issuer"] = Issuer,
            ["AgentMcp:FinanceRejectionReason:ApiTokens:Audience"] = ErpAudience,
            ["AgentMcp:FinanceRejectionReason:ApiTokens:SigningKey"] = signingKey,
        })
        {
            builder.UseSetting(name, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(logging => logging.AddFakeLogging());
            services.AddSingleton<TimeProvider>(Time);
            services.AddHttpClient(McpEndpoint.BackendClient).ConfigurePrimaryHttpMessageHandler(() => Console);
        });
    }

    /// <summary>An Agent's token as Entra issues it to an Agent identity; change any part to show what is refused.</summary>
    public string AgentToken(string? azp = AgentId, string[]? roles = null, string audience = AgentAudience, byte[]? signingKey = null)
    {
        var claims = new Dictionary<string, object>();
        if (azp is not null)
        {
            claims["azp"] = azp;
        }

        claims["roles"] = roles ?? [Role];
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>The Estate health-check agent's token as Entra issues it to its agent identity.</summary>
    public string HealthCheckToken(string? azp = HealthCheckAgentId, string[]? roles = null) =>
        AgentToken(azp, roles ?? [HealthCheckRole], HealthCheckAudience);

    /// <summary>The Customer service agent's token as Entra issues it to its agent identity.</summary>
    public string CustomerServiceToken(string? azp = CustomerServiceAgentId, string[]? roles = null) =>
        AgentToken(azp, roles ?? [CustomerServiceRole], CustomerServiceAudience);

    /// <summary>The Carrier invoice matching agent's token as Entra issues it to its agent identity.</summary>
    public string FinanceToken(string? azp = FinanceAgentId, string[]? roles = null) =>
        AgentToken(azp, roles ?? [FinanceRole], FinanceAudience);

    /// <summary>The Planner agent's token as Entra issues it to its agent identity.</summary>
    public string PlannerToken(string? azp = PlannerAgentId, string[]? roles = null, string? audience = null) =>
        AgentToken(azp, roles ?? [PlannerRole], audience ?? PlannerAudience);

    /// <summary>The Customer service reply agent's token as Entra issues it to its agent identity.</summary>
    public string CustomerServiceReplyToken(string? azp = CustomerServiceReplyAgentId, string[]? roles = null, string? audience = null) =>
        AgentToken(azp, roles ?? [CustomerServiceReplyRole], audience ?? CustomerServiceReplyAudience);

    /// <summary>The Carrier invoice rejection reason agent's token as Entra issues it to its agent identity.</summary>
    public string FinanceRejectionReasonToken(string? azp = FinanceRejectionReasonAgentId, string[]? roles = null, string? audience = null) =>
        AgentToken(azp, roles ?? [FinanceRejectionReasonRole], audience ?? FinanceRejectionReasonAudience);

    public HttpClient AgentClient(string? token = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? AgentToken());
        return client;
    }
}
