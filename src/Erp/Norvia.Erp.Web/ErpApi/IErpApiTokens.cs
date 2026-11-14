namespace Norvia.Erp.Web.ErpApi;

/// <summary>A token for the ERP API, as the ERP UI's own identity, holding the <c>MasterData.Manage</c> role.</summary>
internal interface IErpApiTokens
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
