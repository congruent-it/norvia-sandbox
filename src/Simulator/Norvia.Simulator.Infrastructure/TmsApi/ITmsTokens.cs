namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>A token for the TMS API as the Simulator's own identity, holding the role the Planner's actions need.</summary>
internal interface ITmsTokens
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
