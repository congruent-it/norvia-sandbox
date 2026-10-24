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
}
