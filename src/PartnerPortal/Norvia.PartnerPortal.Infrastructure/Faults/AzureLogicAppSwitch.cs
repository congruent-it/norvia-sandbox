using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// Starts and stops the Logic App's site through Azure Resource Manager as the portal's managed identity, which holds the custom role
/// <c>Norvia Logic App Switch</c> on that one resource: it can read the site and start and stop it, and do nothing else to the estate.
/// </summary>
internal sealed class AzureLogicAppSwitch : ILogicAppSwitch
{
    private const string ApiVersion = "2024-11-01";
    private static readonly string[] Scopes = ["https://management.azure.com/.default"];

    private readonly HttpClient _http;
    private readonly ManagedIdentityCredential _identity;
    private readonly string _site;

    public AzureLogicAppSwitch(HttpClient http, IOptions<FaultsOptions> options)
    {
        var settings = options.Value;
        var entra = settings.Entra ?? throw new InvalidOperationException("Faults:Entra isn't configured.");
        _http = http;
        _identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId));
        _site = $"https://management.azure.com{settings.LogicApp.ResourceGroupId!.TrimEnd('/')}/providers/Microsoft.Web/sites/{settings.LogicApp.Name}";
    }

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Get, $"{_site}?api-version={ApiVersion}", cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var site = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        return !string.Equals(site?["properties"]?["state"]?.GetValue<string>(), "Stopped", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(HttpMethod.Post, $"{_site}/{(enabled ? "start" : "stop")}?api-version={ApiVersion}", cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string uri, CancellationToken cancellationToken)
    {
        var token = await _identity.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }
}
