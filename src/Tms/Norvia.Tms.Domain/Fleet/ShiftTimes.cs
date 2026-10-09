namespace Norvia.Tms.Domain.Fleet;

/// <summary>When a Driver's Shift starts and ends on a day, in Norvia's local time.</summary>
public sealed record ShiftTimes
{
    public ShiftTimes(TimeOnly start, TimeOnly end)
    {
        if (start >= end)
        {
            throw new ArgumentException("A Shift ends after it starts.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }
}
