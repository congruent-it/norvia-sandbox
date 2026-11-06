using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>
/// The port locally (ADR 0041): the personas agent's loop runs in process with the scripted fake model, so a local plan and CI are
/// deterministic and need no Azure. The agent has no tools. A failure of any kind, or a run past the timeout, comes back as unavailable
/// with the failure code, and the plan falls back to the built-in pool. The log names the profile and the category, never a text.
/// </summary>
internal sealed partial class LocalAgentClient(IOptions<AgentsOptions> options, ILogger<LocalAgentClient> logger) : IAgentClient
{
    private const string AgentName = "simulator-personas";

    public async Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid().ToString("N");
        try
        {
            var definition = AgentDefinitions.Load(AgentName);
            var runner = new AgentRunner(PersonaTextScript.Model(), NoTools.Source, new AgentRunnerOptions(TimeSpan.FromSeconds(options.Value.TimeoutSeconds)));
            var question = new AgentQuestion(
                PersonaQuestions.Ask(request.Profile.Id, request.Profile.Style, request.Profile.Language, request.Category.ToString(), request.Count), CorrelationId: string.Empty, runId);
            var result = await runner.RunTextsAsync(definition, question, cancellationToken);

            LogGenerated(logger, AgentName, runId, request.Profile.Id, request.Category);
            return Result<IReadOnlyList<string>>.Success(result.Texts);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failureCode = exception is AgentRunException run ? run.FailureCode : AgentFailureCodes.AgentUnavailable;
            LogFailed(logger, exception, AgentName, runId, request.Profile.Id, request.Category, failureCode);
            return Result<IReadOnlyList<string>>.Unavailable(failureCode);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} run {RunId} wrote a pool for {ProfileId} {Category}")]
    private static partial void LogGenerated(ILogger logger, string agentName, string runId, string profileId, Norvia.Simulator.Domain.Personas.TextCategory category);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentName} run {RunId} failed for {ProfileId} {Category} ({FailureCode})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string agentName, string runId, string profileId, Norvia.Simulator.Domain.Personas.TextCategory category, string failureCode);
}
