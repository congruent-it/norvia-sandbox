namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS API answered an assignment.</summary>
public abstract record AssignAnswer
{
    private AssignAnswer()
    {
    }

    /// <summary>The Transport Order is Planned now, with its one Shipment, so it leaves the list waiting for planning.</summary>
    /// <param name="LoadWarning">The Own fleet load check for the collection day, including this Shipment, when it is Over capacity or outside the Planning horizon. A warning only.</param>
    public sealed record Planned(string ShipmentNumber, string? LoadWarning = null) : AssignAnswer;

    /// <summary>The TMS didn't assign it, such as an order another Planner has planned already, with its readable reason.</summary>
    public sealed record Refused(string Reason) : AssignAnswer;
}
