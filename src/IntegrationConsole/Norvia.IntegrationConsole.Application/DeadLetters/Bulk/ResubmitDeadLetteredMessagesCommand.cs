using System.Security.Claims;
using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

/// <summary>
/// Resubmits several dead-lettered messages of one queue or subscription, as an operator holding
/// <c>IntegrationConsole.Resubmit</c>. They go back in their original enqueue order, each resubmitted and audited on its own;
/// one that fails doesn't stop the rest. A selection spanning two entities is refused.
/// </summary>
/// <param name="Caller">The operator, from their token.</param>
/// <param name="Messages">The selection, in any order.</param>
public sealed record ResubmitDeadLetteredMessagesCommand(ClaimsPrincipal Caller, IReadOnlyList<DeadLetterSelection> Messages)
    : ICommand<Result<BulkOutcome>>;
