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
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AgentRateLimit>();
// The tool list is the contract file the Bicep reads too (ADR 0040), so the two cannot differ.
builder.Services.AddSingleton(_ => ToolListContract.Parse(ReadEmbedded("integration-operations.tools.json")));

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
