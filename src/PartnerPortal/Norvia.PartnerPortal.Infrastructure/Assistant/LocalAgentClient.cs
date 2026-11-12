using System.Reflection;
using Ardalis.Result;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

/// <summary>
/// The port locally (ADR 0041): the Agent runs in process with the scripted fake model, with no tool and no credential. Whatever goes wrong is
/// <c>AgentUnavailable</c> and the Shipper is told so (ADR 0040). It never logs the input, which holds the Shipper's question.
/// </summary>
internal sealed partial class LocalAgentClient(IOptions<AssistantOptions> options, ILogger<LocalAgentClient> logger, IChatClient? model = null) : IAgentClient
{
    private static readonly Lazy<AgentDefinition> Definition = new(Load);

    private readonly IChatClient _model = model ?? ShipperTrackingAssistantScript.Model();

    public async Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var definition = Definition.Value with { Model = Definition.Value.Model with { MaxOutputTokens = request.MaxOutputTokens } };
            var runner = new AgentRunner(_model, NoTools.Source, new AgentRunnerOptions(TimeSpan.FromSeconds(options.Value.TimeoutSeconds)));
            var run = await runner.RunReplyAsync(definition, new AgentQuestion(request.Input.Text, request.RunId, request.RunId), cancellationToken);
            var reply = TrackingAssistantReplies.Parse(run.Reply);
            return new AssistantReply(reply.Answer, [.. reply.Fields.Select(field => new AssistantClaim(field))], reply.NoInformation, run.Tokens);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception.GetType().Name);
            return Result<AssistantReply>.Unavailable(AssistantFailureCodes.AgentUnavailable);
        }
    }

    internal static AgentDefinition Load()
    {
        using var stream = typeof(LocalAgentClient).Assembly.GetManifestResourceStream("agents/shipper-tracking-assistant.agent.yaml")
            ?? throw new InvalidAgentDefinitionException("There is no definition for the Agent 'shipper-tracking-assistant'.");
        using var reader = new StreamReader(stream);
        return AgentDefinitionLoader.Parse(reader.ReadToEnd());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tracking assistant failed ({ExceptionType})")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
