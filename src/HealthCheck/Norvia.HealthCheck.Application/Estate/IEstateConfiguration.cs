namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// The port to the estate's configuration. Infrastructure implements it: locally a JSON snapshot kept in the repo, so local Findings
/// come from the snapshot and not from a running estate; in Azure, Resource Graph and Service Bus's management plane as the app's own identity.
/// </summary>
public interface IEstateConfiguration
{
    Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken);
}
