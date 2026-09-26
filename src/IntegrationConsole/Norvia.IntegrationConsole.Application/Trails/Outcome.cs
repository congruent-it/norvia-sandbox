namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>A hop's outcome in the tracking contract (coding guidelines §7), from best to worst.</summary>
public enum Outcome
{
    Succeeded,
    Refused,
    Retrying,
    DeadLettered,
    Failed,
}
