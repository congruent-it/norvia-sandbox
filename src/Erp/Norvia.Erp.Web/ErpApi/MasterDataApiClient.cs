using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.ErpApi;

/// <summary>
/// Sends Finance's master-data saves to the ERP API as the ERP UI's own identity. A save is a <c>PUT</c> of the whole record, so a
/// repeat of it, a retry included, changes nothing. Each save goes in a span of its own, which is the UI's hop on the trail.
/// </summary>
internal sealed partial class MasterDataApiClient(HttpClient http, IErpApiTokens tokens, ErpActivities activities, ILogger<MasterDataApiClient> logger)
    : IMasterDataCommands
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<MasterDataAnswer> SaveShipperAsync(string id, string name, bool active, CancellationToken cancellationToken) =>
        PutAsync("Shipper", "shippers", id, new { name, active }, cancellationToken);

    public Task<MasterDataAnswer> SaveCarrierAsync(string id, string name, bool active, CancellationToken cancellationToken) =>
        PutAsync("Carrier", "carriers", id, new { name, active }, cancellationToken);

    public Task<MasterDataAnswer> SaveVehicleAsync(string id, string registration, string type, int payloadKg, int palletPlaces, bool active, CancellationToken cancellationToken) =>
        PutAsync("Vehicle", "vehicles", id, new { registration, type, payloadKg, palletPlaces, active }, cancellationToken);

    public Task<MasterDataAnswer> SaveDriverAsync(string id, string name, IReadOnlyList<string> licences, bool active, CancellationToken cancellationToken) =>
        PutAsync("Driver", "drivers", id, new { name, licences, active }, cancellationToken);

    public Task<MasterDataAnswer> SaveRateCardAsync(string id, string shipperId, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken) =>
        PutAsync("Rate Card", "rate-cards", id, new { shipperId, versions }, cancellationToken);

    public Task<MasterDataAnswer> SaveCarrierRateCardAsync(string id, string carrierId, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken) =>
        PutAsync("Carrier Rate Card", "carrier-rate-cards", id, new { carrierId, versions }, cancellationToken);

    private async Task<MasterDataAnswer> PutAsync(string kind, string resource, string id, object body, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity($"Save {kind}", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.MasterData, [TrackingKeys.PartyId] = id });

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/v1/{resource}/{Uri.EscapeDataString(id)}", UriKind.Relative))
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Created or HttpStatusCode.OK:
                var saved = await response.Content.ReadFromJsonAsync<JsonNode>(Json, cancellationToken)
                    ?? throw new HttpRequestException($"The ERP API answered a save of {kind} {id} without a body.");
                var outcome = Enum.Parse<MasterDataOutcome>(saved["outcome"]?.GetValue<string>() ?? "", ignoreCase: true);
                var revision = saved.AsObject().Select(property => property.Value).OfType<JsonObject>().Select(record => record["revision"]).FirstOrDefault(node => node is not null)?.GetValue<int>()
                    ?? throw new HttpRequestException($"The ERP API answered a save of {kind} {id} without a revision.");
                using (logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded }))
                {
                    LogSaved(kind, id, outcome, revision);
                }

                return new MasterDataAnswer.Saved(outcome, revision);
            case HttpStatusCode.BadRequest:
                var problem = await response.Content.ReadFromJsonAsync<Problem>(Json, cancellationToken);
                var reason = problem?.Detail ?? $"The {kind} can't be saved.";
                using (logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused }))
                {
                    LogRefused(kind, id, reason);
                }

                return new MasterDataAnswer.Refused(reason);
            default:
                response.EnsureSuccessStatusCode();
                throw new HttpRequestException($"The ERP API answered a save of {kind} {id} with {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    private sealed record Problem(string? Detail, string? Code);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {Id} {Outcome} at Master-data revision {Revision}")]
    private partial void LogSaved(string kind, string id, MasterDataOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving {Kind} {Id} was refused: {Reason}")]
    private partial void LogRefused(string kind, string id, string reason);
}
