namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>A token for an application's fault endpoints, as the presenter: it holds the Demo.Fault role.</summary>
internal interface IFaultTokens
{
    Task<string> GetTokenAsync(FaultOwnerOptions owner, CancellationToken cancellationToken);
}
