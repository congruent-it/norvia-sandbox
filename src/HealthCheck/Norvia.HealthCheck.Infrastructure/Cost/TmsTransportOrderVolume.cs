using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Cost;

namespace Norvia.HealthCheck.Infrastructure.Cost;

/// <summary>
/// Reads the Transport Orders received per day from the TMS API as the app's own managed identity. The identity holds the one role that
/// opens that count and nothing else of the TMS.
/// </summary>
internal sealed class TmsTransportOrderVolume(HttpClient http, TokenCredential credential, IOptions<TmsApiOptions> options) : ITransportOrderVolume
{
    public async Task<IReadOnlyDictionary<DateOnly, int>> CountPerDayAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([options.Value.Scope]), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Create(CultureInfo.InvariantCulture, $"/v1/transport-orders/volume?from={firstDay:yyyy-MM-dd}&to={lastDay:yyyy-MM-dd}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var days = await response.Content.ReadFromJsonAsync<List<DayCount>>(cancellationToken) ?? [];
        return days.ToDictionary(day => day.Date, day => day.Count);
    }

    private sealed record DayCount(DateOnly Date, int Count);
}
