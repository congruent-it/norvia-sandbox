namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>A dead-lettered message with its body in full, which holds personal data.</summary>
/// <param name="Message">The message as a peek lists it.</param>
/// <param name="Body">The body as it was sent, read as UTF-8 text.</param>
/// <param name="Properties">The application properties, by name, with their values as text.</param>
public sealed record RecordedDeadLetterBody(RecordedDeadLetter Message, string Body, IReadOnlyDictionary<string, string> Properties);
