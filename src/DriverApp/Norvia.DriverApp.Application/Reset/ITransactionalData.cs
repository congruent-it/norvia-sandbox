namespace Norvia.DriverApp.Application.Reset;

/// <summary>The Driver app's database as a reset sees it: transactional records go, reference data stays, and what the seed file provides is in force again.</summary>
public interface ITransactionalData
{
    /// <summary>Deletes the Trips the Driver app was sent, the Drivers' reports and the outbox Messages, and puts the demo settings back to their defaults.</summary>
    Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken);
}
