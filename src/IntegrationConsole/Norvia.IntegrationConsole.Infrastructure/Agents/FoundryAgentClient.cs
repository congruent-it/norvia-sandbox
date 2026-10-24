using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>
/// The port in Azure (ADR 0040): asks the Agent published in the Foundry project through the Responses API. Foundry runs the loop, the
/// Agent calls the API Management MCP server as its own identity, and the correlation ID of the Message trail goes down as the
/// structured input the definition turns into the <c>X-Correlation-ID</c> header of every tool call, so the Agent's calls join the trail
/// (<c>inputs.correlationIdHeader</c> of the definition). The call stores nothing (<c>store: false</c>) and starts no conversation, and the response is
/// deleted again as soon as the answer is read, best effort; the nightly sweeper is the safety net (docs/operations/foundry.md).
///
/// The run is a span of the console like the local one, with a run ID, and a short timeout and no retry. Whatever goes wrong, the operator
/// is told the assistant is unavailable and nothing else changes. Foundry tells the host which tools it called, so the host checks
/// what the in-process runner enforces itself: only the tools the definition lists, no more calls than it allows, none refused.
/// </summary>
internal sealed partial class FoundryAgentClient(
    HttpClient http,
    IOptions<AgentClientOptions> options,
    TokenCredential credential,
    IRunIds runIds,
    AgentActivities activities,
    ILogger<FoundryAgentClient> logger) : IAgentClient
{
    /// <summary>The <c>flow</c> of an Agent run on the Message trail, the same as a local one.</summary>
    public const string Flow = LocalAgentClient.Flow;

    /// <summary>A failed delete is coded, never thrown (docs/operations/foundry.md, "Conversation cleanup").</summary>
    public const string ConversationNotDeleted = "ConversationNotDeleted";

    /// <summary>The structured input the deploy script declares and puts in the tool's header (agents/README.md).</summary>
    internal const string CorrelationInput = "correlationId";

    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(5);

    public async Task<Result<Suggestion>> AskAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        var runId = runIds.Next();
        using var span = activities.Source.StartActivity("Agent run");
        span?.SetTag("agent.name", request.AgentName);
        span?.SetTag("agent.run.id", runId);
        span?.SetTag("correlationId", request.CorrelationId);
        using var trail = logger.BeginScope(new Dictionary<string, object?> { ["correlationId"] = request.CorrelationId, ["flow"] = Flow });

        string? responseId = null;
        try
        {
            var definition = AgentDefinitions.Load(request.AgentName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            var response = await CreateResponseAsync(definition, request, settings.Foundry, deadline.Token);
            responseId = response["id"]?.GetValue<string>();
            span?.SetTag("agent.response.id", responseId);

            var suggestion = Read(definition, response);
            using var succeeded = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Succeeded" });
            LogAnswered(logger, request.AgentName, runId);
            return suggestion;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failureCode = exception is AgentRunException run ? run.FailureCode : AgentFailureCodes.AgentUnavailable;
            span?.SetStatus(ActivityStatusCode.Error, failureCode);
            using var failed = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Failed", ["failureCode"] = failureCode });
            LogFailed(logger, exception, request.AgentName, runId);
            return Result<Suggestion>.Unavailable(failureCode);
        }
        finally
        {
            await DeleteAsync(settings.Foundry, responseId, span);
        }
    }

    private async Task<JsonNode> CreateResponseAsync(AgentDefinition definition, AgentRequest request, FoundryOptions foundry, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([foundry.Scope]), cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(foundry, "responses"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["agent_reference"] = new JsonObject { ["type"] = "agent_reference", ["name"] = request.AgentName },
                ["input"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = request.Question }),
                // Nothing is kept by the service, and no conversation is made: an Agent keeps no memory between runs (ADR 0040).
                ["store"] = false,
                // The cap on the answer and on tool calls is the definition's, as the in-process runner holds to it.
                ["max_output_tokens"] = definition.Model.MaxOutputTokens,
                ["max_tool_calls"] = definition.Model.MaxToolCalls,
                // The definition declares this input and uses it as the tool calls' correlation header.
                ["structured_inputs"] = new JsonObject { [CorrelationInput] = request.CorrelationId },
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

    private static Suggestion Read(AgentDefinition definition, JsonNode response)
    {
        if (response["error"] is JsonObject { Count: > 0 } || response["status"]?.GetValue<string>() is "failed" or "cancelled" or "incomplete")
        {
            throw new AgentRunException("The response did not complete.");
        }

        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            switch (item?["type"]?.GetValue<string>())
            {
                case "mcp_call":
                    var tool = item["name"]?.GetValue<string>() ?? string.Empty;
                    if (!definition.Tools.Names.Contains(tool, StringComparer.Ordinal))
                    {
                        throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The Agent called '{tool}', which the definition doesn't list.");
                    }

                    if (results.Count >= definition.Model.MaxToolCalls)
                    {
                        throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The Agent made more than {definition.Model.MaxToolCalls} tool calls.");
                    }

                    if (item["error"] is { } error && error.GetValueKind() != JsonValueKind.Null && error.ToJsonString() is not ("\"\"" or "{}"))
                    {
                        throw new AgentRunException(AgentFailureCodes.AgentToolRefused, $"The tool '{tool}' was refused.");
                    }

                    // Results are numbered in the order the Agent called its tools, as the in-process runner numbers them.
                    results[$"r{results.Count + 1}"] = tool;
                    break;
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

        return SuggestionParser.Parse(text.ToString(), results);
    }

    // The response was made with store: false, so there is normally nothing to delete and Foundry may say so. Whatever it says, the
    // operator's answer is not held back for it.
    private async Task DeleteAsync(FoundryOptions foundry, string? responseId, Activity? span)
    {
        if (string.IsNullOrEmpty(responseId))
        {
            return;
        }

        try
        {
            using var deadline = new CancellationTokenSource(DeleteTimeout);
            var token = await credential.GetTokenAsync(new TokenRequestContext([foundry.Scope]), deadline.Token);
            using var message = new HttpRequestMessage(HttpMethod.Delete, Endpoint(foundry, $"responses/{Uri.EscapeDataString(responseId)}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await http.SendAsync(message, deadline.Token);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            span?.SetTag("failureCode.cleanup", ConversationNotDeleted);
            LogNotDeleted(logger, exception, ConversationNotDeleted);
        }
    }

    private static Uri Endpoint(FoundryOptions foundry, string path) =>
        new($"{foundry.ProjectEndpoint!.AbsoluteUri.TrimEnd('/')}/openai/v1/{path}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} run {RunId} answered")]
    private static partial void LogAnswered(ILogger logger, string agentName, string runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentName} run {RunId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string agentName, string runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The run's response could not be deleted: {FailureCode}")]
    private static partial void LogNotDeleted(ILogger logger, Exception exception, string failureCode);
}
