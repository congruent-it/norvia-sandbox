using System.Security.Claims;
using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Discard;

/// <summary>
/// Discards a dead-lettered message for a reason, as an operator holding <c>IntegrationConsole.Resubmit</c>: the message is
/// completed on its dead-letter queue and the audit records the reason. Discarding is safe to repeat, as every command is
/// idempotent (coding guidelines §6): a message that is gone is not found, and nothing else happens.
/// </summary>
/// <param name="Caller">The operator, from their token.</param>
/// <param name="Entity">The queue's name, or a subscription's as <c>topic/subscription</c>.</param>
/// <param name="SequenceNumber">The broker's number for the message within its dead-letter queue.</param>
/// <param name="Reason">Why the message is discarded. Required.</param>
public sealed record DiscardDeadLetteredMessageCommand(ClaimsPrincipal Caller, string Entity, long SequenceNumber, string Reason)
    : ICommand<Result<DiscardedDeadLetter>>;
