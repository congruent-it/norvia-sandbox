using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Messages;

/// <summary>
/// One dead-lettered message with its body and application properties, peeked when the operator opens it. The body holds
/// personal data, so it is fetched only here, and never copied anywhere, the console's logs included.
/// </summary>
public sealed record OpenDeadLetteredMessageQuery(string Entity, long SequenceNumber) : IQuery<Result<RecordedDeadLetterBody>>;
