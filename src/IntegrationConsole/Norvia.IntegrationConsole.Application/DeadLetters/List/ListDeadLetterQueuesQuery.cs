using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.List;

/// <summary>Every queue and subscription in the estate with its dead-letter count, those with dead-lettered messages first.</summary>
public sealed record ListDeadLetterQueuesQuery : IQuery<DeadLetterQueueList>;
