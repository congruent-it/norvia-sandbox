using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Web.Shipments;

/// <summary>What the TMS API answered a reassignment.</summary>
public abstract record ReassignAnswer
{
    private ReassignAnswer()
    {
    }

    /// <summary>The Shipment is carried by this now, keeping its Shipment number.</summary>
    /// <param name="LoadWarning">The Own fleet load check for the collection day, including this Shipment, when it is Over capacity or outside the Planning horizon. A warning only.</param>
    public sealed record Reassigned(CarriedByView CarriedBy, string? LoadWarning = null) : ReassignAnswer;

    /// <summary>The TMS didn't reassign it, such as a Shipment released already, with its readable reason.</summary>
    public sealed record Refused(string Reason) : ReassignAnswer;
}
