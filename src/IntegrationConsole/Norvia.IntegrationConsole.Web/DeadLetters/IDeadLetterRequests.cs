using Ardalis.Result;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Bulk;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;

namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>What the dead-letter pages ask of the application.</summary>
public interface IDeadLetterRequests
{
    Task<DeadLetterQueueList> ListAsync(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken);

    Task<Result<DeadLetteredMessageList>> ListMessagesAsync(ListDeadLetteredMessagesQuery query, CancellationToken cancellationToken);

    Task<Result<RecordedDeadLetterBody>> OpenMessageAsync(OpenDeadLetteredMessageQuery query, CancellationToken cancellationToken);

    Task<Result<ResubmittedDeadLetter>> ResubmitAsync(ResubmitDeadLetteredMessageCommand command, CancellationToken cancellationToken);

    Task<Result<DiscardedDeadLetter>> DiscardAsync(DiscardDeadLetteredMessageCommand command, CancellationToken cancellationToken);

    Task<Result<BulkOutcome>> ResubmitManyAsync(ResubmitDeadLetteredMessagesCommand command, CancellationToken cancellationToken);

    Task<Result<BulkOutcome>> DiscardManyAsync(DiscardDeadLetteredMessagesCommand command, CancellationToken cancellationToken);
}
