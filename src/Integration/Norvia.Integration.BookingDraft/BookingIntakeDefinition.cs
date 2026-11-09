using System.Reflection;
using Norvia.Agents.Definitions;

namespace Norvia.Integration.BookingDraft;

/// <summary>The Intake agent's definition file (<c>agents/booking-intake.agent.yaml</c>), read from the assembly it is embedded in.</summary>
public static class BookingIntakeDefinition
{
    public const string Name = "booking-intake";

    public static AgentDefinition Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"agents/{Name}.agent.yaml")
            ?? throw new InvalidAgentDefinitionException($"There is no definition for the Agent '{Name}'.");
        using var reader = new StreamReader(stream);
        return AgentDefinitionLoader.Parse(reader.ReadToEnd());
    }
}
