using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

public sealed class ResubmitDeadLetteredMessagesHandler(ResubmitDeadLetteredMessageHandler one)
    : ICommandHandler<ResubmitDeadLetteredMessagesCommand, Result<BulkOutcome>>
{
    public async ValueTask<Result<BulkOutcome>> Handle(ResubmitDeadLetteredMessagesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await BulkSender.SendAsync(
            command.Messages,
            (entity, sequenceNumber, token) => one.Handle(new ResubmitDeadLetteredMessageCommand(command.Caller, entity, sequenceNumber), token),
            cancellationToken);
    }
}
