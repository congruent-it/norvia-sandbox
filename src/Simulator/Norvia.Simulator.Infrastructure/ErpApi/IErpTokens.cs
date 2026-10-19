namespace Norvia.Simulator.Infrastructure.ErpApi;

/// <summary>A token for the ERP API as the Simulator's own identity, holding the role Finance's actions need.</summary>
internal interface IErpTokens
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
