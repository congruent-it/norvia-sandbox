using System.Diagnostics;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>
/// The port locally (ADR 0041): the Agent's loop runs in process against the MCP stand-in, with the scripted model unless a
/// setting names a deployment. An Agent run is a span of the console, named after the action and started from the activity
/// source named after the application (coding guidelines §7), with a run ID. Its tool calls reach the Message trail as the
/// stand-in's and the agent API's hops, linked to it by the correlation ID the run passes down. A short timeout and no retry.
/// </summary>
internal sealed partial class LocalAgentClient(
    HttpClient http,
    IOptions<AgentClientOptions> options,
    TokenCredential credential,
    IRunIds runIds,
    AgentActivities activities,
    ILogger<LocalAgentClient> logger) : IAgentClient
{
    /// <summary>The <c>flow</c> of an Agent run on the Message trail.</summary>
    public const string Flow = "integration-operations-agent";

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

        try
        {
            var definition = AgentDefinitions.Load(request.AgentName);
            var runner = new AgentRunner(
                AgentModels.For(definition, settings.Model, credential),
                new McpToolSource(http, settings.McpAddress ?? throw new InvalidOperationException("Agents:McpAddress is not set.")),
                new AgentRunnerOptions(TimeSpan.FromSeconds(settings.TimeoutSeconds)));
            var result = await runner.RunAsync(definition, new AgentQuestion(request.Question, request.CorrelationId, runId), cancellationToken);

            using var succeeded = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Succeeded" });
            LogAnswered(logger, request.AgentName, runId, result.ToolCalls.Count);
            return result.Suggestion;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Whatever went wrong, the operator is told the assistant is unavailable and nothing else changes (ADR 0040).
            var failureCode = exception is AgentRunException run ? run.FailureCode : AgentFailureCodes.AgentUnavailable;
            span?.SetStatus(ActivityStatusCode.Error, failureCode);
            using var failed = logger.BeginScope(new Dictionary<string, object?> { ["outcome"] = "Failed", ["failureCode"] = failureCode });
            LogFailed(logger, exception, request.AgentName, runId);
            return Result<Suggestion>.Unavailable(failureCode);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} run {RunId} answered after {ToolCalls} tool calls")]
    private static partial void LogAnswered(ILogger logger, string agentName, string runId, int toolCalls);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentName} run {RunId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string agentName, string runId);
}
