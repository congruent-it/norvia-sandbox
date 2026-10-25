namespace Norvia.HealthCheck.Application.Checks;

/// <summary>How serious a Finding is. A Check gives it; the Agent never changes it.</summary>
public enum Severity
{
    Low,
    Medium,
    High,
    Critical,
}
