using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.DeadLetters;

namespace Norvia.IntegrationConsole.Application.Trails.Open;

public sealed class OpenTrailHandler(ITrailSource source, IDeadLetterQueues queues, TimeProvider time, IOptions<IntegrationRetention> retention)
    : IQueryHandler<OpenTrailQuery, Result<MessageTrail>>
{
    public async ValueTask<Result<MessageTrail>> Handle(OpenTrailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var correlationId = query.CorrelationId?.Trim();
        if (string.IsNullOrEmpty(correlationId))
        {
            return Result.Invalid(new ValidationError(nameof(query.CorrelationId), "Open a Message trail by its correlation ID."));
        }

        // A trail can outlast any search range, as a retry or a resubmit days later continues it, so look as far back
        // as anything is kept.
        var to = time.GetUtcNow();
        var from = to - retention.Value.Period;
        var hops = await source.HopsAsync(new TrailLookup(correlationId, from, to), cancellationToken);

        // Only a dead-lettered hop has a message to look for, so other trails cost no more than before.
        var deadLetters = hops.Any(hop => hop.Outcome == Outcome.DeadLettered)
            ? DeadLetterMatching.Of(
                hops,
                await source.AuditAsync(new TrailLookup(correlationId, from, to), cancellationToken),
                await queues.DeadLetteredAsync(correlationId, cancellationToken))
            : null;

        return new MessageTrail(correlationId, from, to, TrailOutline.Of(hops, deadLetters), BusinessKeysOf(hops));
    }

    // In the tracking contract's order, so the Transport Order leads.
    private static BusinessKey[] BusinessKeysOf(IEnumerable<RecordedHop> hops) =>
    [
        .. hops.SelectMany(hop => hop.BusinessKeys)
            .Distinct()
            .OrderBy(key => key.Kind)
            .ThenBy(key => key.Value, StringComparer.Ordinal),
    ];
}
