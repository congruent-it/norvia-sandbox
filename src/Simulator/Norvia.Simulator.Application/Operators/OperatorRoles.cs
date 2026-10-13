namespace Norvia.Simulator.Application.Operators;

/// <summary>
/// The app roles a Simulator operator holds. Locally the AppHost gives the local operator both; in Azure the estate assigns
/// them to the presenter on the Simulator's app registration.
/// </summary>
public static class OperatorRoles
{
    /// <summary>Configure, preview and start runs, cancel them and clear the history.</summary>
    public const string Run = "Simulator.Run";

    /// <summary>See runs and their logs.</summary>
    public const string Read = "Simulator.Read";
}
