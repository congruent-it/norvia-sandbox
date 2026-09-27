namespace Norvia.Tms.Web.TmsApi;

/// <summary>A token for the TMS API, as the TMS UI's own identity, holding the manage role.</summary>
internal interface ITmsApiTokens
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
