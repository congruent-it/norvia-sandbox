using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;

public sealed class ResubmitDeadLetteredMessageHandler(IDeadLetterQueues queues, IAuditLog audit, TimeProvider time)
    : ICommandHandler<ResubmitDeadLetteredMessageCommand, Result<ResubmittedDeadLetter>>
{
    public async ValueTask<Result<ResubmittedDeadLetter>> Handle(ResubmitDeadLetteredMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The page hides the control from everyone else; this is the check that holds (coding guidelines §8). Without a name
        // the audit couldn't say who.
        if (command.Caller is not { Identity.IsAuthenticated: true, Identity.Name: { Length: > 0 } actor } caller
            || !caller.IsInRole(OperatorRoles.Resubmit))
        {
            return Result.Forbidden();
        }

        if (command.Entity?.Trim() is not { Length: > 0 } entity || command.SequenceNumber < 0)
        {
            return Result.Invalid(new ValidationError(nameof(command.Entity), "Resubmit a message by its queue or subscription and its sequence number."));
        }

        if (await queues.ResubmitAsync(entity, command.SequenceNumber, cancellationToken) is not { } resubmitted)
        {
            return Result.NotFound();
        }

        await audit.WriteAsync(
            new AuditEvent(actor, AuditActions.Resubmit, resubmitted.Entity, resubmitted.OriginalMessageId, resubmitted.CorrelationId, resubmitted.BusinessKeys, time.GetUtcNow(), Agent: command.ActingOn),
            cancellationToken);
        return resubmitted;
    }
}
