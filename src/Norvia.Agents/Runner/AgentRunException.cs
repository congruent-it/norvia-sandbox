namespace Norvia.Agents.Runner;

/// <summary>An Agent run that ended without a Suggestion, with the failure code it is recorded under (<see cref="AgentFailureCodes"/>).</summary>
public sealed class AgentRunException : Exception
{
    public AgentRunException()
        : this(AgentFailureCodes.AgentUnavailable, "The Agent run failed.")
    {
    }

    public AgentRunException(string message)
        : this(AgentFailureCodes.AgentUnavailable, message)
    {
    }

    public AgentRunException(string message, Exception innerException)
        : this(AgentFailureCodes.AgentUnavailable, message, innerException)
    {
    }

    public AgentRunException(string failureCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
