using System.Net.Http.Json;
using Norvia.PartnerPortal.Application.DriverAppFault;

namespace Norvia.PartnerPortal.Infrastructure.DriverApp;

/// <summary>Reads and changes the Driver app's demo settings through its API.</summary>
internal sealed class DriverAppSettingsClient(HttpClient http) : IDriverAppSettings
{
    private const string Path = "v1/settings/unavailable";

    public async Task<bool> IsUnavailableAsync(CancellationToken cancellationToken)
    {
        var setting = await http.GetFromJsonAsync<UnavailableBody>(Path, cancellationToken);
        return setting?.Unavailable ?? false;
    }

    public async Task SetUnavailableAsync(bool unavailable, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync(Path, new UnavailableBody(unavailable), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record UnavailableBody(bool Unavailable);
}
