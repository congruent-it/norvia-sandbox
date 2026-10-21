using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>
/// Lists the estate's namespace through Azure Resource Manager as the console's own identity (the credential), never with the
/// operator's token (ADR 0017). The estate grants that identity Reader on the namespace, which is enough to find every queue
/// and subscription and read their message counts.
/// </summary>
internal sealed class ArmServiceBusEntities(IHttpClientFactory clients, TokenCredential credential, IOptions<EstateServiceBusOptions> options)
    : IServiceBusEntities
{
    public const string ManagementClient = "service-bus-management";

    private const string Management = "https://management.azure.com";
    private const string ApiVersion = "api-version=2024-01-01";
    private static readonly string[] ManagementScopes = [$"{Management}/.default"];

    public async Task<IReadOnlyList<ServiceBusEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ResourceGroupId) || string.IsNullOrWhiteSpace(settings.ServiceBusNamespace))
        {
            throw new InvalidOperationException(
                $"The console isn't connected to an estate: {EstateServiceBusOptions.SectionName}:{nameof(EstateServiceBusOptions.ResourceGroupId)} " +
                $"and {EstateServiceBusOptions.SectionName}:{nameof(EstateServiceBusOptions.ServiceBusNamespace)} are not both set.");
        }

        var name = settings.ServiceBusNamespace;
        var address = $"{Management}{settings.ResourceGroupId.TrimEnd('/')}/providers/Microsoft.ServiceBus/namespaces/{Uri.EscapeDataString(name)}";
        var entities = new List<ServiceBusEntity>();
        foreach (var queue in await ListAsync($"{address}/queues?{ApiVersion}", cancellationToken))
        {
            entities.Add(new ServiceBusEntity(name, Name(queue), null, DeadLetters(queue)));
        }

        foreach (var topic in await ListAsync($"{address}/topics?{ApiVersion}", cancellationToken))
        {
            var topicName = Name(topic);
            foreach (var subscription in await ListAsync($"{address}/topics/{Uri.EscapeDataString(topicName)}/subscriptions?{ApiVersion}", cancellationToken))
            {
                entities.Add(new ServiceBusEntity(name, topicName, Name(subscription), DeadLetters(subscription)));
            }
        }

        return entities;
    }

    private static string Name(JsonNode resource) => resource["name"]?.GetValue<string>() ?? throw new InvalidOperationException("A Service Bus entity has no name.");

    private static long DeadLetters(JsonNode resource) => resource["properties"]?["countDetails"]?["deadLetterMessageCount"]?.GetValue<long>() ?? 0;

    private async Task<List<JsonNode>> ListAsync(string address, CancellationToken cancellationToken)
    {
        var items = new List<JsonNode>();
        string? page = address;
        while (page is not null)
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScopes), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await clients.CreateClient(ManagementClient).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            items.AddRange(answer?["value"] is JsonArray values ? values.OfType<JsonNode>() : []);
            page = answer?["nextLink"]?.GetValue<string>();
        }

        return items;
    }
}
