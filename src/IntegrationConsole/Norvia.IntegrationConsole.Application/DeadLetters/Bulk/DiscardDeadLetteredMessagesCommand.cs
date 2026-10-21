using System.Security.Claims;
using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

/// <summary>
/// Discards several dead-lettered messages of one queue or subscription for one reason, which is recorded on each message's
/// audit event. Each is discarded and audited on its own; one that fails doesn't stop the rest. A selection spanning two
/// entities is refused.
/// </summary>
/// <param name="Caller">The operator, from their token.</param>
/// <param name="Messages">The selection, in any order.</param>
/// <param name="Reason">Why the messages are discarded. Required.</param>
public sealed record DiscardDeadLetteredMessagesCommand(ClaimsPrincipal Caller, IReadOnlyList<DeadLetterSelection> Messages, string Reason)
    : ICommand<Result<BulkOutcome>>;
