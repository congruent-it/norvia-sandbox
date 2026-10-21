using Ardalis.Result;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

internal static class BulkSender
{
    /// <summary>
    /// Sends the selection one message at a time in original enqueue order. Each message is its own use case, so each is
    /// audited on its own, and one that fails is reported and the rest still go. Forbidden holds for every message alike, so
    /// it ends the run at the first.
    /// </summary>
    public static async ValueTask<Result<BulkOutcome>> SendAsync<T>(
        IReadOnlyList<DeadLetterSelection> messages,
        Func<string, long, CancellationToken, ValueTask<Result<T>>> sendOne,
        CancellationToken cancellationToken)
    {
        var selection = SelectionRules.Order(messages);
        if (!selection.IsSuccess)
        {
            return Result.Invalid(selection.ValidationErrors);
        }

        var (entity, ordered) = selection.Value;
        var outcomes = new List<MessageOutcome>(ordered.Count);
        foreach (var message in ordered)
        {
            MessageOutcome outcome;
            try
            {
                var result = await sendOne(entity, message.SequenceNumber, cancellationToken);
                if (result.Status == ResultStatus.Forbidden)
                {
                    return Result.Forbidden();
                }

                outcome = result.Status switch
                {
                    ResultStatus.Ok => new MessageOutcome(message.SequenceNumber, MessageOutcomeStatus.Done),
                    ResultStatus.NotFound => new MessageOutcome(message.SequenceNumber, MessageOutcomeStatus.Gone),
                    _ => new MessageOutcome(message.SequenceNumber, MessageOutcomeStatus.Failed, string.Join(" ", result.ValidationErrors.Select(error => error.ErrorMessage))),
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The detail stays out: an infrastructure failure's text isn't for the page. The message is still on the queue.
                outcome = new MessageOutcome(message.SequenceNumber, MessageOutcomeStatus.Failed, "It is still on the dead-letter queue; try again in a moment.");
            }

            outcomes.Add(outcome);
        }

        return new BulkOutcome(entity, outcomes);
    }
}
