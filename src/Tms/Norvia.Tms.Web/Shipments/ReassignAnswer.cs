using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Web.Shipments;

/// <summary>What the TMS API answered a reassignment.</summary>
public abstract record ReassignAnswer
{
    private ReassignAnswer()
    {
    }

    /// <summary>The Shipment is carried by this now, keeping its Shipment number.</summary>
    public sealed record Reassigned(CarriedByView CarriedBy) : ReassignAnswer;

    /// <summary>The TMS didn't reassign it, such as a Shipment released already, with its readable reason.</summary>
    public sealed record Refused(string Reason) : ReassignAnswer;
}
