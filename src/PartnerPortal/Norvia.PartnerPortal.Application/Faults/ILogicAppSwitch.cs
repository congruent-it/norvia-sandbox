namespace Norvia.PartnerPortal.Application.Faults;

/// <summary>The Driver reports Logic App, which the <c>driver-reports-logic-app-disabled</c> fault disables. In Azure it is the Logic App itself; locally it is the worker that plays it.</summary>
public interface ILogicAppSwitch
{
    /// <summary>Whether the Logic App is enabled now.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    /// <summary>Enables or disables the Logic App. Asking for the state it is in changes nothing.</summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
