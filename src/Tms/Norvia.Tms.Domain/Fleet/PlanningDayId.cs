namespace Norvia.Tms.Domain.Fleet;

/// <summary>A day of the Planning horizon is identified by its date.</summary>
public readonly record struct PlanningDayId(DateOnly Value)
{
    public override string ToString() => Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
