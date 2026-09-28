namespace Norvia.Tms.Web.TransportOrders;

/// <summary>How one Transport Order's assignment went, of the several a Planner assigned in one go.</summary>
/// <param name="Assignee">Who it was assigned to, as the Planner chose them: Own fleet or the Carrier's name.</param>
/// <param name="Answer">What the TMS API answered; null when it couldn't be reached, so the Planner tries again.</param>
public sealed record AssignmentOutcome(string TransportOrderNumber, string Assignee, AssignAnswer? Answer);
