using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

public sealed class DiscardDeadLetteredMessagesHandler(DiscardDeadLetteredMessageHandler one)
    : ICommandHandler<DiscardDeadLetteredMessagesCommand, Result<BulkOutcome>>
{
    public async ValueTask<Result<BulkOutcome>> Handle(DiscardDeadLetteredMessagesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One reason for all, refused before the first message goes rather than once per message.
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Invalid(new ValidationError(nameof(command.Reason), "Give a reason for discarding the messages."));
        }

        return await BulkSender.SendAsync(
            command.Messages,
            (entity, sequenceNumber, token) => one.Handle(new DiscardDeadLetteredMessageCommand(command.Caller, entity, sequenceNumber, command.Reason), token),
            cancellationToken);
    }
}
