using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;

/// <summary>
/// Sends a dead-lettered message back to its queue, or to its topic for that subscription only, as an operator holding <c>IntegrationConsole.Resubmit</c>. The
/// body is never part of it: a copy goes out as it was dead-lettered, and a mapping fault is fixed in the map (ADR 0017).
/// Resubmitting is safe to repeat, as every command is idempotent (coding guidelines §6).
/// </summary>
/// <param name="Caller">The operator, from their token.</param>
/// <param name="Entity">The queue's name, or a subscription's as <c>topic/subscription</c>.</param>
/// <param name="SequenceNumber">The broker's number for the message within its dead-letter queue.</param>
/// <param name="ActingOn">The Agent run whose Suggestion the operator is acting on, which the audit then carries. Null when they act on their own.</param>
public sealed record ResubmitDeadLetteredMessageCommand(ClaimsPrincipal Caller, string Entity, long SequenceNumber, AgentRun? ActingOn = null)
    : ICommand<Result<ResubmittedDeadLetter>>;
