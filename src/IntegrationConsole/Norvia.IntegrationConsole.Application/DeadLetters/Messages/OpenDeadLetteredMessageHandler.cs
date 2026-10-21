using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Messages;

public sealed class OpenDeadLetteredMessageHandler(IDeadLetterQueues queues)
    : IQueryHandler<OpenDeadLetteredMessageQuery, Result<RecordedDeadLetterBody>>
{
    public async ValueTask<Result<RecordedDeadLetterBody>> Handle(OpenDeadLetteredMessageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Entity?.Trim() is not { Length: > 0 } entity || query.SequenceNumber < 0)
        {
            return Result.Invalid(new ValidationError(nameof(query.Entity), "Open a message by its queue or subscription and its sequence number."));
        }

        return await queues.OpenAsync(entity, query.SequenceNumber, cancellationToken) is { } opened
            ? opened
            : Result.NotFound();
    }
}
