using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Messages;

/// <summary>The dead-lettered messages of one queue or subscription, peeked and not received, without their bodies.</summary>
/// <param name="Entity">A queue's name, or a subscription's as <c>topic/subscription</c>.</param>
public sealed record ListDeadLetteredMessagesQuery(string Entity) : IQuery<Result<DeadLetteredMessageList>>;
