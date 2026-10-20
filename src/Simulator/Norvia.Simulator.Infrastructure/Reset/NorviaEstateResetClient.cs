using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Infrastructure.DriverApi;
using Norvia.Simulator.Infrastructure.ErpApi;
using Norvia.Simulator.Infrastructure.TmsApi;

namespace Norvia.Simulator.Infrastructure.Reset;

/// <summary>
/// Calls the reset door of the TMS API, the Driver app API and the ERP API as the Simulator's own identity, and the Partner portal's with its reset key. The doors
/// answer in one shape: GET says whether the application would take a reset, POST resets and says what it deleted, or refuses with a problem whose detail
/// says why.
/// </summary>
internal sealed partial class NorviaEstateResetClient(
    HttpClient http,
    ITmsTokens tmsTokens,
    IDriverTokens driverTokens,
    IErpTokens erpTokens,
    IOptions<TmsApiOptions> tms,
    IOptions<DriverApiOptions> driver,
    IOptions<ErpApiOptions> erp,
    IOptions<PartnerPortalResetOptions> portal,
    ILogger<NorviaEstateResetClient> logger) : IEstateReset
{
    public const string ApiKeyHeader = "X-Api-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AppReadiness> CheckAsync(EstateApp app, CancellationToken cancellationToken)
    {
        try
        {
            using var request = await RequestAsync(HttpMethod.Get, app, cancellationToken);
            if (request is null)
            {
                return new AppReadiness(app, Reachable: false, Enabled: false, BusySubscription: null);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogNotAsked(app, (int)response.StatusCode);
                return new AppReadiness(app, Reachable: false, Enabled: false, BusySubscription: null);
            }

            var answer = await response.Content.ReadFromJsonAsync<ReadinessAnswer>(Json, cancellationToken);
            return answer is null
                ? new AppReadiness(app, Reachable: false, Enabled: false, BusySubscription: null)
                : new AppReadiness(app, Reachable: true, answer.Enabled, answer.BusySubscription);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogNotAsked(app, exception);
            return new AppReadiness(app, Reachable: false, Enabled: false, BusySubscription: null);
        }
    }

    public async Task<AppResetResult> ResetAsync(EstateApp app, CancellationToken cancellationToken)
    {
        try
        {
            using var request = await RequestAsync(HttpMethod.Post, app, cancellationToken);
            if (request is null)
            {
                return AppResetResult.Refused(app, $"The Simulator isn't set up to reach the {EstateApps.NameOf(app)}'s reset.");
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var report = await response.Content.ReadFromJsonAsync<ReportAnswer>(Json, cancellationToken);
                return AppResetResult.Done(app, report?.Deleted ?? []);
            }

            var refusal = await RefusalAsync(response, cancellationToken);
            LogRefused(app, (int)response.StatusCode);
            return AppResetResult.Refused(app, refusal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogNotReset(app, exception);
            return AppResetResult.Refused(app, $"The {EstateApps.NameOf(app)} didn't answer.");
        }
    }

    // Null when the Simulator has nowhere to send the call.
    private async Task<HttpRequestMessage?> RequestAsync(HttpMethod method, EstateApp app, CancellationToken cancellationToken)
    {
        switch (app)
        {
            case EstateApp.Tms:
                return Bearer(method, new Uri(Join(tms.Value.BaseAddress!, "v1/reset")), await tmsTokens.GetTokenAsync(cancellationToken));
            case EstateApp.DriverApp:
                return Bearer(method, new Uri(Join(driver.Value.BaseAddress!, "v1/reset")), await driverTokens.GetResetTokenAsync(cancellationToken));
            case EstateApp.Erp when erp.Value.BaseAddress is not null:
                return Bearer(method, new Uri(Join(erp.Value.BaseAddress!, "v1/reset")), await erpTokens.GetTokenAsync(cancellationToken));
            case EstateApp.PartnerPortal when portal.Value.IsConfigured:
                var request = new HttpRequestMessage(method, new Uri(Join(portal.Value.BaseAddress!, "reset/v1")));
                request.Headers.Add(ApiKeyHeader, portal.Value.ResetApiKey);
                return request;
            default:
                return null;
        }
    }

    private static HttpRequestMessage Bearer(HttpMethod method, Uri address, string token)
    {
        var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Join(Uri baseAddress, string path) => baseAddress.ToString().TrimEnd('/') + "/" + path;

    private static async Task<string> RefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && !IsProblem(response))
        {
            return "It refused the Simulator's credential.";
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemAnswer>(Json, cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }
        catch (JsonException)
        {
            // Not a problem document: the status says all there is.
        }

        return $"It answered {(int)response.StatusCode}.";
    }

    private static bool IsProblem(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType?.Contains("problem", StringComparison.OrdinalIgnoreCase) == true;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {App} couldn't be asked whether it would take a reset: it answered {Status}")]
    private partial void LogNotAsked(EstateApp app, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {App} couldn't be asked whether it would take a reset")]
    private partial void LogNotAsked(EstateApp app, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {App} refused the reset with {Status}")]
    private partial void LogRefused(EstateApp app, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {App} didn't answer the reset")]
    private partial void LogNotReset(EstateApp app, Exception exception);

    private sealed record ReadinessAnswer(bool Enabled, string? BusySubscription);

    private sealed record ReportAnswer(IReadOnlyList<DeletedKind>? Deleted);

    private sealed record ProblemAnswer(string? Detail);
}
