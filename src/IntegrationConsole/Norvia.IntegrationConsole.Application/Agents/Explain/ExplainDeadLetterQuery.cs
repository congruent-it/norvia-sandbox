using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

/// <summary>Explain a dead-lettered message: the Integration operations agent peeks its queue and says why it was dead-lettered.</summary>
/// <param name="Entity">The queue, or the subscription as <c>topic/subscription</c>.</param>
/// <param name="SequenceNumber">The broker's number for the message within its dead-letter queue.</param>
/// <param name="CorrelationId">The Message trail the message carries, or null when it carries none.</param>
public sealed record ExplainDeadLetterQuery(string Entity, long SequenceNumber, string? CorrelationId = null) : IQuery<Result<Suggestion>>;
