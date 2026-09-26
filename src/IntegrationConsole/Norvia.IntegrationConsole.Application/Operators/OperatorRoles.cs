namespace Norvia.IntegrationConsole.Application.Operators;

/// <summary>
/// The app roles an Integration operator holds on the console's app registration in Entra ID. The estate Bicep defines
/// them and assigns the presenter both.
/// </summary>
public static class OperatorRoles
{
    /// <summary>Search, Message trails, run details and the bodies in them.</summary>
    public const string Read = "IntegrationConsole.Read";

    /// <summary>Also resubmit and discard dead-lettered messages (slice 2).</summary>
    public const string Resubmit = "IntegrationConsole.Resubmit";
}
