using System.Reflection;

namespace Norvia.Agents.UnitTests;

/// <summary>The definition and the tool lists of the repo, embedded so the tests read the real files.</summary>
internal static class Embedded
{
    public static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string IntegrationOperationsDefinition => Read("agents/integration-operations.agent.yaml");

    public static string IntegrationOperationsTools => Read("agent-mcp/integration-operations.tools.json");

    public static string EstateHealthCheckDefinition => Read("agents/estate-health-check.agent.yaml");

    public static string SimulatorPersonasDefinition => Read("agents/simulator-personas.agent.yaml");

    public static string HealthCheckTools => Read("agent-mcp/health-check.tools.json");

    public static string CustomerServiceDefinition => Read("agents/customer-service.agent.yaml");

    public static string CustomerServiceTools => Read("agent-mcp/customer-service.tools.json");

    public static string CustomerServiceReplyDefinition => Read("agents/customer-service-reply.agent.yaml");

    public static string CustomerServiceReplyTools => Read("agent-mcp/customer-service-reply.tools.json");

    public static string PlannerDefinition => Read("agents/planner.agent.yaml");

    public static string PlannerTools => Read("agent-mcp/planner.tools.json");

    public static string FinanceMatchingDefinition => Read("agents/finance-matching.agent.yaml");

    public static string FinanceMatchingTools => Read("agent-mcp/finance-matching.tools.json");

    public static string FinanceRejectionReasonDefinition => Read("agents/finance-rejection-reason.agent.yaml");

    public static string FinanceRejectionReasonTools => Read("agent-mcp/finance-rejection-reason.tools.json");
}
