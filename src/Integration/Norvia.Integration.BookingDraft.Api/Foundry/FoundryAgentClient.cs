using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft.Api.Foundry;

/// <summary>
/// The port in Azure (ADR 0040): asks the Intake agent published in the Foundry project <c>norvia-intake</c> through the Responses API, as the host's
/// own managed identity. The Agent has no tool and its identity holds no role beyond calling the model. The call stores nothing (<c>store: false</c>)
/// and starts no conversation; the response is deleted before the draft is returned, best effort (docs/operations/foundry.md, "Conversation
/// cleanup"), and a failed delete is coded on the run's span and never reaches the Shipper. The nightly sweeper is the safety net.
///
/// The run is a span of the host and a hop on the Shipper's Message trail with the agent's identity, a run ID, a short timeout and no retry.
/// Neither the Shipper's text nor the Agent's reply is ever logged or put on a span: the trail holds keys only (coding guidelines §7).
/// </summary>
internal sealed partial class FoundryAgentClient(
    HttpClient http,
    IOptions<BookingDraftHostOptions> options,
    TokenCredential credential,
    BookingDraftActivities activities,
    ILogger<FoundryAgentClient> logger) : IAgentClient
{
    /// <summary>A failed delete is coded, never thrown (docs/operations/foundry.md, "Conversation cleanup").</summary>
    public const string ConversationNotDeleted = "ConversationNotDeleted";

    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(5);

    public async Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var settings = options.Value;
        using var span = activities.Source.StartActivity("Agent run");
        span?.SetTag("agent.name", BookingIntakeDefinition.Name);
        span?.SetTag("agent.run.id", question.RunId);
        span?.SetTag("correlationId", question.CorrelationId);
        using var trail = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = question.CorrelationId,
            ["flow"] = BookingDraftEndpoint.Flow,
            ["agentId"] = BookingIntakeDefinition.Name,
            ["runId"] = question.RunId,
        });

        string? responseId = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            var response = await CreateResponseAsync(question, settings.Foundry, deadline.Token);
            responseId = response["id"]?.GetValue<string>();
            span?.SetTag("agent.response.id", responseId);

            var reply = Read(response);
            // Deleted first, and only then returned: the Shipper's text is not left in the project while the draft travels.
            await DeleteAsync(settings.Foundry, responseId, span);
            responseId = null;
            using var succeeded = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Succeeded" });
            LogAnswered(logger);
            return reply;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failureCode = exception is AgentRunException run ? run.FailureCode : AgentFailureCodes.AgentUnavailable;
            span?.SetStatus(ActivityStatusCode.Error, failureCode);
            using var failed = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Failed", ["failureCode"] = failureCode });
            LogFailed(logger, exception.GetType().Name);
            await DeleteAsync(settings.Foundry, responseId, span);
            throw;
        }
    }

    private async Task<JsonNode> CreateResponseAsync(AgentQuestion question, FoundryOptions foundry, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([foundry.Scope]), cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(foundry, "responses"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["agent_reference"] = new JsonObject { ["type"] = "agent_reference", ["name"] = BookingIntakeDefinition.Name },
                ["input"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = question.Text }),
                // Nothing is kept by the service, and no conversation is made: an Agent keeps no memory between runs (ADR 0040).
                ["store"] = false,
                // The cap on the answer is the definition's, as the in-process runner holds to it. The Agent has no tool, so there is no cap on calls.
                ["max_output_tokens"] = Definition.Value.Model.MaxOutputTokens,
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

    private static readonly Lazy<Norvia.Agents.Definitions.AgentDefinition> Definition = new(BookingIntakeDefinition.Load);

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
                case "message":
                    foreach (var part in item["content"]?.AsArray() ?? [])
                    {
                        if (part?["type"]?.GetValue<string>() == "output_text")
                        {
                            text.Append(part["text"]?.GetValue<string>());
                        }
                    }

                    break;
                case "mcp_call" or "function_call" or "file_search_call" or "web_search_call" or "code_interpreter_call":
                    // The Agent has no tool. If it called one, someone gave it one, which the host does not accept.
                    throw new AgentRunException(AgentFailureCodes.AgentToolRefused, "The Agent called a tool, which its definition does not have.");
                default:
                    break;
            }
        }

        return text.ToString();
    }

    // The response was made with store: false, so there is normally nothing to delete and Foundry may say so. Whatever it says, the
    // Shipper's draft is not held back for it.
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
            LogNotDeleted(logger, ConversationNotDeleted, exception.GetType().Name);
        }
    }

    private static Uri Endpoint(FoundryOptions foundry, string path) =>
        new($"{foundry.ProjectEndpoint!.AbsoluteUri.TrimEnd('/')}/openai/v1/{path}");

    [LoggerMessage(Level = LogLevel.Information, Message = "The Intake agent answered")]
    private static partial void LogAnswered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Intake agent failed ({ExceptionType})")]
    private static partial void LogFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The run's response could not be deleted: {FailureCode} ({ExceptionType})")]
    private static partial void LogNotDeleted(ILogger logger, string failureCode, string exceptionType);
}
