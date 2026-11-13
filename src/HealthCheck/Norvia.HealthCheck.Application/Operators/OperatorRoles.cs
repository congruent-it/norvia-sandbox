namespace Norvia.HealthCheck.Application.Operators;

/// <summary>The app roles on the Estate health check's app registration (#371). The Integration operator holds them.</summary>
public static class OperatorRoles
{
    /// <summary>Run an Estate health check and see what it found.</summary>
    public const string Run = "HealthCheck.Run";

    /// <summary>Read health check results. Reserved for the report (#394), which will show a result to someone who may not run one.</summary>
    public const string Read = "HealthCheck.Read";

    /// <summary>See the spend page (#506): the estate's spend per resource next to Transport Order volume. Not the cost Agent's call role, which is its own.</summary>
    public const string Cost = "HealthCheck.Cost";
}
