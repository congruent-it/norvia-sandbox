using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>A Planner's note on an Exception on a Shipment's timeline (P7).</summary>
public sealed record ExceptionNoteView(string Text, string Planner, DateTimeOffset AddedAt)
{
    public static ExceptionNoteView Of(ExceptionNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new(note.Text, note.Planner, note.AddedAt);
    }
}
