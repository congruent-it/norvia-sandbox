using Ardalis.Result;
using Mediator;

namespace Norvia.HealthCheck.Application.Failures;

/// <summary>List the queues and subscriptions whose dead-letter queue holds messages, with whether an alert watches each.</summary>
public sealed record ListDeadLetterQueuesQuery : IQuery<Result<DeadLetterQueuesReport>>;

/// <param name="Estate">What the facts describe, such as the snapshot's name.</param>
/// <param name="Queues">Only the entities that hold dead-lettered messages, the fullest first.</param>
public sealed record DeadLetterQueuesReport(string Estate, IReadOnlyList<DeadLetteredEntity> Queues);

public sealed record DeadLetteredEntity(string Name, int DeadLetteredMessages, bool HasDeadLetterAlert);
