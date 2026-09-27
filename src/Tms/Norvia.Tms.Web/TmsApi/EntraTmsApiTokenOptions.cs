namespace Norvia.Tms.Web.TmsApi;

public sealed class EntraTmsApiTokenOptions
{
    /// <summary>The TMS API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the TMS UI's managed identity, which holds the TMS API's manage role.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    public bool IsComplete => !string.IsNullOrWhiteSpace(Scope) && !string.IsNullOrWhiteSpace(ManagedIdentityClientId);
}
