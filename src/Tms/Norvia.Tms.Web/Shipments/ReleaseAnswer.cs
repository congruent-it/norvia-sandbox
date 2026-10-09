namespace Norvia.Tms.Web.Shipments;

/// <summary>What the TMS API answered a release.</summary>
public abstract record ReleaseAnswer
{
    private ReleaseAnswer()
    {
    }

    /// <summary>The Shipment is Released now, so it leaves the Planner's list of Shipments to release.</summary>
    public sealed record Released : ReleaseAnswer;

    /// <summary>The TMS didn't release it, such as a Shipment released already, with its readable reason and the code that says which rule.</summary>
    /// <param name="Code">The refusal's code, such as <c>NoMatchingLane</c>; null when the TMS named none.</param>
    public sealed record Refused(string Reason, string? Code = null) : ReleaseAnswer;
}
