namespace Norvia.Erp.Application.Operators;

/// <summary>
/// The app roles a user of the ERP holds. Locally the AppHost gives the local user the role; in Azure the estate assigns it to the
/// presenter on the ERP's app registration.
/// </summary>
public static class ErpRoles
{
    /// <summary>Finance uses the ERP: its lists today, Invoice runs and Credit notes in the slices after this one.</summary>
    public const string Finance = "Finance";
}
