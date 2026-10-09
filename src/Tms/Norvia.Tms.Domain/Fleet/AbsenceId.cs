namespace Norvia.Tms.Domain.Fleet;

public readonly record struct AbsenceId(Guid Value)
{
    public static AbsenceId New() => new(Guid.CreateVersion7());
}
