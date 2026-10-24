using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.Agents.Explain;

/// <summary>What needs attention on the dead-letter queues: the Integration operations agent groups the non-empty ones by cause. A fixed question, with no free text.</summary>
public sealed record SummariseDeadLettersQuery : IQuery<Result<Suggestion>>;
