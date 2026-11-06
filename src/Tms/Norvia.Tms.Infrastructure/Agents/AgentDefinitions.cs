using System.Reflection;
using Norvia.Agents.Definitions;

namespace Norvia.Tms.Infrastructure.Agents;

/// <summary>The Agents' definition files, read from the assembly they are embedded in.</summary>
internal static class AgentDefinitions
{
    public static AgentDefinition Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"agents/{name}.agent.yaml")
            ?? throw new InvalidAgentDefinitionException($"There is no definition for the Agent '{name}'.");
        using var reader = new StreamReader(stream);
        return AgentDefinitionLoader.Parse(reader.ReadToEnd());
    }
}
