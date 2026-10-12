namespace Norvia.PartnerPortal.Application.DriverAppFault;

/// <summary>The Driver app's demo settings, which the presenter panel switches. The Driver app is its own application.</summary>
public interface IDriverAppSettings
{
    Task<bool> IsUnavailableAsync(CancellationToken cancellationToken);

    Task SetUnavailableAsync(bool unavailable, CancellationToken cancellationToken);
}
