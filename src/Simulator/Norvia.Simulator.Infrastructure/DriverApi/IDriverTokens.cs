namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>A token to call the Driver app API as one Driver: the Driver ID is its one app role (ADR 0037).</summary>
internal interface IDriverTokens
{
    Task<string> GetTokenAsync(string driverId, CancellationToken cancellationToken);
}
