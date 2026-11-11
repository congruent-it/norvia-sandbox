using System.Reflection;
using Microsoft.Extensions.Options;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalAgentMcp;

// Locally there is no API Management, so this plays its MCP server for the Agents (ADR 0041).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Data annotations don't look inside the token options, so they are checked here too.
builder.Services.AddOptions<AgentMcpOptions>().BindConfiguration(AgentMcpOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.Tokens.IsComplete, "AgentMcp:Tokens needs an Issuer, an Audience and a SigningKey.")
    .Validate(options => options.ConsoleTokens.IsComplete, "AgentMcp:ConsoleTokens needs an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.HealthCheck is null || (options.HealthCheck.Api is { IsAbsoluteUri: true } && options.HealthCheck.Tokens.IsComplete && options.HealthCheck.ApiTokens.IsComplete),
        "AgentMcp:HealthCheck needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.CustomerService is null || (options.CustomerService.Api is { IsAbsoluteUri: true } && options.CustomerService.Tokens.IsComplete && options.CustomerService.ApiTokens.IsComplete),
        "AgentMcp:CustomerService needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.CustomerServiceReply is null || (options.CustomerServiceReply.Api is { IsAbsoluteUri: true } && options.CustomerServiceReply.Tokens.IsComplete && options.CustomerServiceReply.ApiTokens.IsComplete),
        "AgentMcp:CustomerServiceReply needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.FinanceMatching is null || (options.FinanceMatching.Api is { IsAbsoluteUri: true } && options.FinanceMatching.Tokens.IsComplete && options.FinanceMatching.ApiTokens.IsComplete),
        "AgentMcp:FinanceMatching needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.FinanceRejectionReason is null || (options.FinanceRejectionReason.Api is { IsAbsoluteUri: true } && options.FinanceRejectionReason.Tokens.IsComplete && options.FinanceRejectionReason.ApiTokens.IsComplete),
        "AgentMcp:FinanceRejectionReason needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.Planner is null || (options.Planner.Api is { IsAbsoluteUri: true } && options.Planner.Tokens.IsComplete && options.Planner.ApiTokens.IsComplete),
        "AgentMcp:Planner needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .Validate(
        options => options.PlannerNotification is null || (options.PlannerNotification.Api is { IsAbsoluteUri: true } && options.PlannerNotification.Tokens.IsComplete && options.PlannerNotification.ApiTokens.IsComplete),
        "AgentMcp:PlannerNotification needs an absolute Api, Tokens and ApiTokens, each with an Issuer, an Audience and a SigningKey.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AgentRateLimit>();
// The tool lists are the contract files the Bicep reads too (ADR 0040), so the two cannot differ.
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("integration-operations.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("health-check.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("customer-service.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("customer-service-reply.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("planner.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("planner-notification.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("finance-matching.tools.json")));
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("finance-rejection-reason.tools.json")));
builder.Services.AddSingleton<AgentServers>();

// A short wait and no retry: an Agent is never on a critical path, and the standard resilience handler would retry (ADR 0040).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
builder.Services.AddHttpClient(McpEndpoint.BackendClient, http => http.Timeout = TimeSpan.FromSeconds(10)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapAgentMcp();

await app.RunAsync();

static string ReadEmbedded(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

/// <summary>Lets the tests start the stand-in.</summary>
public partial class Program;
