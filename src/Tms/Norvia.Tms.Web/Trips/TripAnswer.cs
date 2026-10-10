using Norvia.Tms.Application.Trips;

namespace Norvia.Tms.Web.Trips;

/// <summary>What the TMS API answered a Planner's action on a Draft Trip (P11).</summary>
public abstract record TripAnswer
{
    private TripAnswer()
    {
    }

    /// <summary>The TMS created the Draft, with this number. <paramref name="Warnings"/> says what to know about its Shipments, when it has any.</summary>
    public sealed record Created(string Number, IReadOnlyList<TripWarning>? Warnings = null) : TripAnswer;

    /// <summary>The TMS put the Shipments on the Draft; <paramref name="Warnings"/> says what to know about the Trip now, such as a delivery window outside its day.</summary>
    public sealed record Added(IReadOnlyList<TripWarning> Warnings) : TripAnswer;

    /// <summary>The TMS changed or discarded the Draft.</summary>
    public sealed record Done : TripAnswer;

    /// <summary>The TMS didn't, such as a Vehicle that has another Trip that day, with its readable reason.</summary>
    public sealed record Refused(string Reason) : TripAnswer;
}
