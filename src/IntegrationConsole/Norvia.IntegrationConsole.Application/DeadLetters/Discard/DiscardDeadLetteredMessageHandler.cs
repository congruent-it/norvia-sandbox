using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Discard;

public sealed class DiscardDeadLetteredMessageHandler(IDeadLetterQueues queues, IAuditLog audit, TimeProvider time)
    : ICommandHandler<DiscardDeadLetteredMessageCommand, Result<DiscardedDeadLetter>>
{
    public async ValueTask<Result<DiscardedDeadLetter>> Handle(DiscardDeadLetteredMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The page hides the control from everyone else; this is the check that holds (coding guidelines §8). Without a name
        // the audit couldn't say who.
        if (command.Caller is not { Identity.IsAuthenticated: true, Identity.Name: { Length: > 0 } actor } caller
            || !caller.IsInRole(OperatorRoles.Resubmit))
        {
            return Result.Forbidden();
        }

        var errors = new List<ValidationError>();
        var entity = command.Entity?.Trim();
        if (string.IsNullOrEmpty(entity) || command.SequenceNumber < 0)
        {
            errors.Add(new ValidationError(nameof(command.Entity), "Discard a message by its queue or subscription and its sequence number."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            errors.Add(new ValidationError(nameof(command.Reason), "Give a reason for discarding the message."));
        }

        if (errors.Count > 0)
        {
            return Result.Invalid(errors);
        }

        if (await queues.DiscardAsync(entity!, command.SequenceNumber, cancellationToken) is not { } discarded)
        {
            return Result.NotFound();
        }

        await audit.WriteAsync(
            new AuditEvent(actor, AuditActions.Discard, discarded.Entity, discarded.MessageId, discarded.CorrelationId, discarded.BusinessKeys, time.GetUtcNow(), reason, command.ActingOn),
            cancellationToken);
        return discarded;
    }
}
