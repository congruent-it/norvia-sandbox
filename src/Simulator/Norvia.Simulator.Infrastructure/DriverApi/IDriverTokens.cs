namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>A token to call the Driver app API as one Driver: the Driver ID is its one app role (ADR 0037).</summary>
internal interface IDriverTokens
{
    Task<string> GetTokenAsync(string driverId, CancellationToken cancellationToken);

    /// <summary>A token for the Simulator's own identity, holding the role of the Driver app API's reset door.</summary>
    Task<string> GetResetTokenAsync(CancellationToken cancellationToken);
}
