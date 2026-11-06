using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents.Runner;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>
/// The port in Azure (ADR 0040): asks the personas agent published in the Foundry project through the Responses API, as the Simulator's own
/// managed identity. The agent has no tool and its own identity holds no role, so there is nothing for it to call. The call stores nothing
/// (<c>store: false</c>) and starts no conversation, caps the answer at the definition's <c>maxOutputTokens</c>, and has a short timeout and
/// no retry. The generation is a span of the app with a run ID; the profile and the category are on it, never a text. Whatever goes wrong comes
/// back as unavailable, and the plan falls back to the built-in pool.
/// </summary>
internal sealed partial class FoundryAgentClient(
    HttpClient http,
    IOptions<AgentsOptions> options,
    TokenCredential credential,
    SimulatorActivities activities,
    ILogger<FoundryAgentClient> logger) : IAgentClient
{
    private const string AgentName = "simulator-personas";

    public async Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        var runId = Guid.NewGuid().ToString("N");
        using var span = activities.Source.StartActivity("Agent run");
        span?.SetTag("agent.name", AgentName);
        span?.SetTag("agent.run.id", runId);
        span?.SetTag("persona.profile", request.Profile.Id);
        span?.SetTag("persona.category", request.Category.ToString());

        try
        {
            var definition = AgentDefinitions.Load(AgentName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            var question = PersonaQuestions(request);
            var response = await CreateResponseAsync(question, definition.Model.MaxOutputTokens, settings.Foundry, deadline.Token);
            span?.SetTag("agent.response.id", response["id"]?.GetValue<string>());

            var texts = TextsParser.Parse(Read(response));
            LogGenerated(logger, AgentName, runId, request.Profile.Id, request.Category);
            return Result<IReadOnlyList<string>>.Success(texts);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failureCode = exception is AgentRunException run ? run.FailureCode : AgentFailureCodes.AgentUnavailable;
            span?.SetStatus(ActivityStatusCode.Error, failureCode);
            LogFailed(logger, exception, AgentName, runId, request.Profile.Id, request.Category, failureCode);
            return Result<IReadOnlyList<string>>.Unavailable(failureCode);
        }
    }

    private static string PersonaQuestions(PoolRequest request) =>
        Norvia.Agents.Scripts.PersonaQuestions.Ask(request.Profile.Id, request.Profile.Style, request.Profile.Language, request.Category.ToString(), request.Count);

    private async Task<JsonNode> CreateResponseAsync(string question, int maxOutputTokens, FoundryOptions foundry, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([foundry.Scope]), cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{foundry.ProjectEndpoint!.AbsoluteUri.TrimEnd('/')}/openai/v1/responses"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["agent_reference"] = new JsonObject { ["type"] = "agent_reference", ["name"] = AgentName },
                ["input"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = question }),
                // Nothing is kept by the service, and no conversation is made: an Agent keeps no memory between runs (ADR 0040).
                ["store"] = false,
                ["max_output_tokens"] = maxOutputTokens,
            }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AgentRunException($"Foundry answered HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken) ?? throw new AgentRunException("Foundry answered with nothing.");
    }

    private static string Read(JsonNode response)
    {
        if (response["error"] is JsonObject { Count: > 0 } || response["status"]?.GetValue<string>() is "failed" or "cancelled" or "incomplete")
        {
            throw new AgentRunException("The response did not complete.");
        }

        var text = new StringBuilder();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            switch (item?["type"]?.GetValue<string>())
            {
                case "mcp_call" or "function_call":
                    throw new AgentRunException(AgentFailureCodes.AgentToolRefused, "The agent called a tool, and it has none.");
                case "message":
                    foreach (var part in item["content"]?.AsArray() ?? [])
                    {
                        if (part?["type"]?.GetValue<string>() == "output_text")
                        {
                            text.Append(part["text"]?.GetValue<string>());
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return text.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} run {RunId} wrote a pool for {ProfileId} {Category}")]
    private static partial void LogGenerated(ILogger logger, string agentName, string runId, string profileId, TextCategory category);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentName} run {RunId} failed for {ProfileId} {Category} ({FailureCode})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string agentName, string runId, string profileId, TextCategory category, string failureCode);
}
