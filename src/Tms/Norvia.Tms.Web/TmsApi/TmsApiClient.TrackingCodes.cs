using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.TmsApi;

// The Planner's reading and replacing of a tracking code (ADR 0052). The code is a credential: it is never logged or put in a span.
internal sealed partial class TmsApiClient
{
    public async Task<TrackingCodeAnswer> GetTrackingCodeAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Read tracking code", ActivityKind.Internal);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/tracking-code", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        return await TrackingCodeAnswerAsync(request, "reading a tracking code", cancellationToken);
    }

    public async Task<TrackingCodeAnswer> ReplaceTrackingCodeAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Replace tracking code", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/tracking-code/replace", cancellationToken);
        return await TrackingCodeAnswerAsync(request, "replacing a tracking code", cancellationToken);
    }

    private async Task<TrackingCodeAnswer> TrackingCodeAnswerAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var view = await response.Content.ReadFromJsonAsync<TrackingCodeBody>(Json, cancellationToken)
                    ?? throw new HttpRequestException($"The TMS API answered {what} without a body.");
                return new TrackingCodeAnswer.Shown(view.TrackingCode);
            case HttpStatusCode.NotFound:
                return new TrackingCodeAnswer.NotFound();
            default:
                throw Unexpected(response, what);
        }
    }

    private sealed record TrackingCodeBody(string TransportOrderNumber, string? TrackingCode);
}
