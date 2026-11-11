namespace Norvia.Tms.Web.UiTests.PlannerAgent;

internal static class PlannerAgentTimeouts
{
    /// <summary>The assistant reads through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.</summary>
    public const float Answer = 30_000;
}
