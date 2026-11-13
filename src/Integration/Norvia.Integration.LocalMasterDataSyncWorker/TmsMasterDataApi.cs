using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalMasterDataSyncWorker;

/// <summary>What the TMS API answered: the status (zero for no answer) and, for a refusal, its stable code and message.</summary>
internal sealed record TmsAnswer(int StatusCode, string? Code = null, string? Message = null)
{
    public bool Succeeded => HttpStatus.IsSuccess(StatusCode);

    public bool IsRetryable => HttpStatus.IsTransient(StatusCode);

    /// <summary>The workflow's dead-letter reason: unavailable for an unanswered or transient call, else refused (ADR 0043).</summary>
    public string? FailureCode => Succeeded ? null : IsRetryable ? MasterDataSyncFailureCodes.TmsUnavailable : MasterDataSyncFailureCodes.TmsCallRefused;

    public string Description => Message
        ?? (IsRetryable ? "The TMS API did not answer after the retries." : "The TMS API refused the master-data record.");
}

/// <summary>
/// Replaces a record in the TMS with the TMS API as the E3 Logic App does: the record whole, the event's correlation ID and a token of
/// the one identity holding the replace role.
/// </summary>
internal sealed partial class TmsMasterDataApi(HttpClient http, IOptions<MasterDataSyncWorkerOptions> options, TimeProvider time, ILogger<TmsMasterDataApi> logger)
{
    private const string ReplaceRole = "MasterData.Replace";

    /// <param name="path">The entity's collection and ID, such as <c>/v1/shippers/SHP-0001</c>.</param>
    /// <param name="record">The JSON the TMS command takes.</param>
    public async Task<TmsAnswer> ReplaceAsync(string path, string record, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
            {
                Content = new StringContent(record, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, ReplaceRole));

            using var response = await http.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (HttpStatus.IsSuccess(status))
            {
                return new(status);
            }

            var problem = Problem(await response.Content.ReadAsStringAsync(cancellationToken));
            return new(status, Text(problem, "code"), Text(problem, "detail") ?? Text(problem, "title"));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return new(0);
        }
    }

    private static JsonObject? Problem(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS API couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
