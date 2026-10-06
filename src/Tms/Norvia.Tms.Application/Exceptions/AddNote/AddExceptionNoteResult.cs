namespace Norvia.Tms.Application.Exceptions.AddNote;

/// <summary>What adding a note did.</summary>
/// <param name="CorrelationId">The note's own Message trail.</param>
public sealed record AddExceptionNoteResult(string ExceptionNumber, string ShipmentNumber, int NumberOfNotes, string CorrelationId);
