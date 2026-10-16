using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Infrastructure.DriverApi;

namespace Norvia.Simulator.Infrastructure.Entra;

/// <summary>
/// In Azure each Driver is an Entra client app that holds their Driver ID as its one app role on the Driver app API and trusts the Simulator's managed
/// identity (a federated credential), so the Simulator asks for the Driver's token with a token of its own identity instead of a client secret (ADR 0042).
/// The Driver app takes the Driver ID from that role, as it does from a Driver's own token (ADR 0037).
/// </summary>
internal sealed class EntraDriverTokens : IDriverTokens
{
    private const string TokenExchangeScope = "api://AzureADTokenExchange/.default";

    private readonly EntraDriverTokenOptions _entra;
    private readonly ManagedIdentityCredential _simulatorIdentity;
    private readonly ConcurrentDictionary<string, ClientAssertionCredential> _drivers = new();

    public EntraDriverTokens(IOptions<DriverApiOptions> options)
    {
        _entra = options.Value.Entra ?? throw new InvalidOperationException("DriverApp:Entra isn't configured.");
        _simulatorIdentity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_entra.ManagedIdentityClientId));
    }

    public async Task<string> GetTokenAsync(string driverId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        var driver = _drivers.GetOrAdd(driverId, id => new ClientAssertionCredential(_entra.TenantId, _entra.ClientIdOf(id), SimulatorIdentityTokenAsync));
        var token = await driver.GetTokenAsync(new TokenRequestContext([_entra.Scope]), cancellationToken);
        return token.Token;
    }

    private async Task<string> SimulatorIdentityTokenAsync(CancellationToken cancellationToken) =>
        (await _simulatorIdentity.GetTokenAsync(new TokenRequestContext([TokenExchangeScope]), cancellationToken)).Token;
}
