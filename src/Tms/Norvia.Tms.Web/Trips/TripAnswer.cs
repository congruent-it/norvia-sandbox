namespace Norvia.Tms.Web.Trips;

/// <summary>What the TMS API answered a Planner's action on a Draft Trip (P11).</summary>
public abstract record TripAnswer
{
    private TripAnswer()
    {
    }

    /// <summary>The TMS created the Draft, with this number.</summary>
    public sealed record Created(string Number) : TripAnswer;

    /// <summary>The TMS changed or discarded the Draft.</summary>
    public sealed record Done : TripAnswer;

    /// <summary>The TMS didn't, such as a Vehicle that has another Trip that day, with its readable reason.</summary>
    public sealed record Refused(string Reason) : TripAnswer;
}
