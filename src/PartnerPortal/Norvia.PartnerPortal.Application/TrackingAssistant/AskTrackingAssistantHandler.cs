using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>
/// Answers a Shipper's question from what the Norvia Partner API returns to the Persona itself (ADR 0049). The portal fetches the data with the
/// Persona's token and gives it to an Agent that has no tool, then checks the answer in code before the Shipper sees it. A run is a span of the
/// portal with the Persona, the run, the fetches' correlation IDs, the tokens and the outcome. Neither the question nor the answer is logged.
/// </summary>
public sealed partial class AskTrackingAssistantHandler(
    IPersonaDirectory personas,
    INorviaPartnerApi partnerApi,
    IAgentClient agent,
    AssistantLimiter limiter,
    AssistantLimits limits,
    IAssistantRunIds runIds,
    AssistantActivities activities,
    TimeProvider time,
    ILogger<AskTrackingAssistantHandler> logger) : ICommandHandler<AskTrackingAssistantCommand, AssistantAnswer>
{
    /// <summary>The <c>flow</c> of an assistant run.</summary>
    public const string Flow = "shipper-tracking-assistant";

    /// <summary>The Agent's name, which is also the <c>agentId</c> of its spans.</summary>
    public const string AgentName = "shipper-tracking-assistant";

    public async ValueTask<AssistantAnswer> Handle(AskTrackingAssistantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var language = AssistantLanguage.Detect(command.Question);
        var runId = runIds.NewRunId();
        using var span = activities.Source.StartActivity("Ask tracking assistant");
        span?.SetTag("flow", Flow);
        span?.SetTag("partyId", command.PersonaId.Value);
        span?.SetTag("agentId", AgentName);
        span?.SetTag("runId", runId);
        using var trail = logger.BeginScope(new Dictionary<string, object?> { ["flow"] = Flow, ["partyId"] = command.PersonaId.Value, ["runId"] = runId });

        var correlationIds = new List<string>();
        var tokens = 0;
        var answer = await RunAsync(command, language, runId, correlationIds, t => tokens = t, cancellationToken);

        span?.SetTag("agent.tokens", tokens);
        span?.SetTag("fetch.calls", correlationIds.Count);
        span?.SetTag("fetch.correlationIds", string.Join(',', correlationIds));
        span?.SetTag("outcome", answer.Outcome.ToString());
        if (answer.FailureCode is not null)
        {
            span?.SetTag("failureCode", answer.FailureCode);
            if (answer.Outcome == AssistantOutcome.Failed)
            {
                span?.SetStatus(ActivityStatusCode.Error, answer.FailureCode);
            }
        }

        LogRun(logger, answer.Outcome, answer.FailureCode ?? "none", tokens);
        return answer;
    }

    private async Task<AssistantAnswer> RunAsync(
        AskTrackingAssistantCommand command, AssistantLanguageKind language, string runId, List<string> correlationIds, Action<int> setTokens, CancellationToken cancellationToken)
    {
        if (personas.Find(command.PersonaId) is not { PartyType: PartyType.Shipper } shipper)
        {
            return new AssistantAnswer(AssistantOutcome.Refused, AssistantTexts.NoInformation(language), null, runId);
        }

        switch (limiter.Admit(command.PersonaId, command.Question.Length))
        {
            case Admission.RateLimited:
                return new AssistantAnswer(AssistantOutcome.Refused, AssistantTexts.RateLimited(language), AssistantFailureCodes.AgentRateLimited, runId);
            case Admission.EstateBudgetUsed:
                return new AssistantAnswer(AssistantOutcome.Refused, AssistantTexts.Unavailable(language), AssistantFailureCodes.AgentUnavailable, runId);
            default:
                break;
        }

        var data = await FetchAsync(shipper, command, correlationIds, cancellationToken);
        if (data is null)
        {
            return new AssistantAnswer(AssistantOutcome.Failed, AssistantTexts.TrackingUnavailable(language), AssistantFailureCodes.TmsUnavailable, runId);
        }

        var input = AssistantInputBuilder.Build(command.Question, command.History, data, time.GetUtcNow(), limits);
        var result = await agent.AskAsync(new AssistantRequest(runId, input, limits.MaxOutputTokens), cancellationToken);
        if (!result.IsSuccess)
        {
            return new AssistantAnswer(AssistantOutcome.Failed, AssistantTexts.Unavailable(language), AssistantFailureCodes.AgentUnavailable, runId);
        }

        var reply = result.Value;
        limiter.Record(command.PersonaId, reply.TokensUsed);
        setTokens(reply.TokensUsed);
        if (!AnswerCheck.Passes(reply, input))
        {
            return new AssistantAnswer(AssistantOutcome.Failed, AssistantTexts.NoInformation(language), AssistantFailureCodes.AgentAnswerRejected, runId);
        }

        return new AssistantAnswer(AssistantOutcome.Answered, reply.NoInformation ? AssistantTexts.NoInformation(language) : reply.Answer, null, runId);
    }

    // The open order, the first page of the list, and at most one named order: three calls at most. Null when tracking could not be reached.
    private async Task<TrackingData?> FetchAsync(Persona shipper, AskTrackingAssistantCommand command, List<string> correlationIds, CancellationToken cancellationToken)
    {
        ShipperTrackingDetail? open = null;
        if (command.OpenOrderNumber is { Length: > 0 } number)
        {
            var detail = await partnerApi.GetTransportOrderAsync(shipper, number, cancellationToken);
            Note(detail, correlationIds);
            if (Failed(detail))
            {
                return null;
            }

            open = detail.Value;
        }

        var list = await partnerApi.ListTransportOrdersAsync(shipper, new ShipperTrackingFilter(), cancellationToken);
        Note(list, correlationIds);
        if (Failed(list))
        {
            return null;
        }

        IReadOnlyList<ShipperTrackingSummary> orders = [.. (list.Value ?? []).Take(limits.MaxOrders)];
        ShipperTrackingDetail? found = null;
        if (TrackingFetchPlan.Extra(command.Question, open, orders) is { } extra)
        {
            var named = extra.Kind == ExtraFetchKind.GetNumber
                ? await partnerApi.GetTransportOrderAsync(shipper, extra.Value, cancellationToken)
                : await partnerApi.FindTransportOrderAsync(shipper, extra.Value, cancellationToken);
            Note(named, correlationIds);
            if (Failed(named))
            {
                return null;
            }

            found = named.Value;
        }

        return new TrackingData(open, orders, found);
    }

    // Unavailable and a refusal (the subscription's 429) leave the assistant with no data; "not found" is an answer.
    private static bool Failed<T>(TrackingAnswer<T> answer) => answer.Outcome is TrackingOutcome.Unavailable or TrackingOutcome.Refused;

    private static void Note<T>(TrackingAnswer<T> answer, List<string> correlationIds)
    {
        if (answer.MessageId is { Length: > 0 } id)
        {
            correlationIds.Add(id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking assistant run ended {Outcome} ({FailureCode}) using {Tokens} tokens")]
    private static partial void LogRun(ILogger logger, AssistantOutcome outcome, string failureCode, int tokens);
}
