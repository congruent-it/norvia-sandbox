namespace Norvia.Tms.Web.Capacity;

/// <summary>What the TMS API answered a Planner's edit of the Own fleet's availability (P10).</summary>
public abstract record CapacityAnswer
{
    private CapacityAnswer()
    {
    }

    /// <summary>The TMS recorded the edit.</summary>
    public sealed record Done : CapacityAnswer;

    /// <summary>The TMS didn't record it, such as an edit of a closed day, with its readable reason.</summary>
    public sealed record Refused(string Reason) : CapacityAnswer;
}
