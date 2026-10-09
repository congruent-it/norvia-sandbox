namespace Norvia.Tms.Domain.Fleet;

public readonly record struct OffTheRoadDayId(Guid Value)
{
    public static OffTheRoadDayId New() => new(Guid.CreateVersion7());
}
