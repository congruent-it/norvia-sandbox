using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>Where the trail pages link to.</summary>
public static class TrailLinks
{
    /// <summary>The Message trail with this correlation ID.</summary>
    public static string Trail(string correlationId) => $"/trails/{Uri.EscapeDataString(correlationId)}";

    /// <summary>
    /// A search for a business key, over everything the estate keeps, so the operator can follow a Shipment across trails:
    /// trails don't chain, business keys join them.
    /// </summary>
    public static string Search(BusinessKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"/?key={Uri.EscapeDataString(key.Value)}&within=all";
    }

    /// <summary>A Logic App run's detail, with its steps fetched live.</summary>
    public static string Run(WorkflowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return $"/runs/{Uri.EscapeDataString(run.LogicApp)}/{Uri.EscapeDataString(run.Workflow)}/{Uri.EscapeDataString(run.RunId)}";
    }
}
