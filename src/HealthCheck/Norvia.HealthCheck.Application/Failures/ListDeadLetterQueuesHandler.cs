using Ardalis.Result;
using Mediator;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Failures;

public sealed class ListDeadLetterQueuesHandler(IEstateConfiguration estate) : IQueryHandler<ListDeadLetterQueuesQuery, Result<DeadLetterQueuesReport>>
{
    public async ValueTask<Result<DeadLetterQueuesReport>> Handle(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var facts = await estate.ReadAsync(cancellationToken);
        return new DeadLetterQueuesReport(
            facts.Name,
            [
                .. facts.ServiceBusEntities
                    .Where(entity => entity.DeadLetteredMessages > 0)
                    .OrderByDescending(entity => entity.DeadLetteredMessages)
                    .ThenBy(entity => entity.Name, StringComparer.Ordinal)
                    .Select(entity => new DeadLetteredEntity(entity.Name, entity.DeadLetteredMessages!.Value, entity.HasDeadLetterAlert)),
            ]);
    }
}
