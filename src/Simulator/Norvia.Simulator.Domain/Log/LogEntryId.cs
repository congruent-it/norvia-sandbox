namespace Norvia.Simulator.Domain.Log;

public readonly record struct LogEntryId(Guid Value)
{
    public static LogEntryId New() => new(Guid.CreateVersion7());
}
