using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents.Runner;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

/// <summary>
/// The port in Azure (ADR 0049): asks the Shipper tracking assistant published in the Foundry project through the Responses API, as the portal's
/// own managed identity. The Agent has no tool and its identity holds no role, so there is nothing for it to call. The call stores nothing
/// (<c>store: false</c>), so there is no response to delete, and starts no conversation. It caps the answer at the request's output tokens, and has
/// a short timeout and no retry. The tokens Foundry reports go back to the portal's budget counters. Whatever goes wrong is
/// <c>AgentUnavailable</c>. It never logs the input, which holds the Shipper's question.
/// </summary>
internal sealed partial class FoundryAgentClient(
    HttpClient http,
    IOptions<AssistantOptions> options,
    TokenCredential credential,
    ILogger<FoundryAgentClient> logger) : IAgentClient
{
    public async Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            var response = await CreateResponseAsync(request, settings.Foundry, deadline.Token);
            var (text, reportedTokens) = Read(response);
            // A response without usage is charged at the cap on the answer, so the daily budgets cannot be skipped by it.
            var tokens = reportedTokens ?? request.MaxOutputTokens;
            var reply = TrackingAssistantReplies.Parse(text);
            return new AssistantReply(reply.Answer, [.. reply.Fields.Select(field => new AssistantClaim(field))], reply.NoInformation, tokens);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception.GetType().Name, request.RunId);
            return Result<AssistantReply>.Unavailable(AssistantFailureCodes.AgentUnavailable);
        }
    }

    private async Task<JsonNode> CreateResponseAsync(AssistantRequest request, FoundryOptions foundry, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([foundry.Scope]), cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{foundry.ProjectEndpoint!.AbsoluteUri.TrimEnd('/')}/openai/v1/responses"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["agent_reference"] = new JsonObject { ["type"] = "agent_reference", ["name"] = AskTrackingAssistantHandler.AgentName },
                ["input"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = request.Input.Text }),
                // Nothing is kept by the service, and no conversation is made: a question is a single response (ADR 0049).
                ["store"] = false,
                ["max_output_tokens"] = request.MaxOutputTokens,
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

    private static (string Text, int? Tokens) Read(JsonNode response)
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
                    throw new AgentRunException(AgentFailureCodes.AgentToolRefused, "The Agent called a tool, and it has none.");
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

        var usage = response["usage"]?["total_tokens"];
        return (text.ToString(), usage?.GetValue<int>());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tracking assistant failed ({ExceptionType}) in run {RunId}")]
    private static partial void LogFailed(ILogger logger, string exceptionType, string runId);
}
