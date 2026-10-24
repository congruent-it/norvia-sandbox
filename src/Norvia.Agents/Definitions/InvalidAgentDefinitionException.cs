namespace Norvia.Agents.Definitions;

/// <summary>A definition file or contract file that doesn't say what it must. A bug in a versioned file, never an outcome to handle.</summary>
public sealed class InvalidAgentDefinitionException : Exception
{
    public InvalidAgentDefinitionException()
    {
    }

    public InvalidAgentDefinitionException(string message)
        : base(message)
    {
    }

    public InvalidAgentDefinitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
