namespace Norvia.Tms.Web.Exceptions;

/// <summary>
/// Who the TMS UI names as the Planner on what it sends: the Shipper notification's Norvia contact and the Exception's
/// notes. The UI has no login, only the Role switcher (CONTEXT.md), so every Planner is the demo's Planner.
/// </summary>
public static class PlannerName
{
    public const string Demo = "Anna de Vries";
}
