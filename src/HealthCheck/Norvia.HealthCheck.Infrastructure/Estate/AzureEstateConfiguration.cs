using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>
/// The Azure adapter of <see cref="IEstateConfiguration"/>: the same facts the snapshots hold, read from the real estate as the app's own
/// managed identity, which holds Reader on the estate's resource group (never the Agent, #371). Azure Resource Graph answers which Azure
/// Monitor metric alert rules exist and what each watches; the namespace's queues and subscriptions come from Service Bus's management
/// plane, because Resource Graph does not hold them. It only reads.
///
/// An entity has a dead-letter alert when an enabled metric alert on the namespace watches <c>DeadletteredMessages</c> for it, by an
/// <c>EntityName</c> dimension that includes it, or with none, which watches them all. The subscriptions that infra/estate/alert-exemptions.json
/// exempts are left out, as <c>alerts.bicep</c> leaves them out: nobody reads them.
/// </summary>
internal sealed class AzureEstateConfiguration(
    IHttpClientFactory clients,
    TokenCredential credential,
    IOptions<AzureEstateOptions> options) : IEstateConfiguration
{
    public const string ManagementClient = "estate-management";

    /// <summary>The estate's name in the facts: it is the real one.</summary>
    public const string EstateName = "azure";

    private const string Management = "https://management.azure.com";
    private const string ServiceBusApiVersion = "api-version=2024-01-01";
    private const string ResourceGraphAddress = $"{Management}/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";
    private const string DeadLetterMetric = "DeadletteredMessages";
    private const string EntityDimension = "EntityName";
    private static readonly string[] ManagementScopes = [$"{Management}/.default"];

    public async Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var namespaceId = $"{settings.ResourceGroupId.TrimEnd('/')}/providers/Microsoft.ServiceBus/namespaces/{settings.ServiceBusNamespace}";

        var watching = (await ReadAlertsAsync(settings, cancellationToken))
            .Where(alert => alert.Enabled && alert.Scopes.Contains(namespaceId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var entities = (await ReadEntitiesAsync(namespaceId, cancellationToken))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new ServiceBusEntity(name, watching.Exists(alert => alert.Watches(name))))
            .ToList();
        return new EstateConfiguration(EstateName, entities);
    }

    // The metric alert rules of the estate's resource group, as Resource Graph holds them.
    private async Task<List<MetricAlert>> ReadAlertsAsync(AzureEstateOptions settings, CancellationToken cancellationToken)
    {
        var parts = settings.ResourceGroupId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query =
            "resources | where type =~ 'microsoft.insights/metricalerts' " +
            $"| where resourceGroup =~ '{parts[3].Replace("'", "\\'", StringComparison.Ordinal)}' " +
            "| project name, enabled = tobool(properties.enabled), scopes = properties.scopes, criteria = properties.criteria";
        var alerts = new List<MetricAlert>();
        string? skipToken = null;
        do
        {
            var options = new JsonObject { ["resultFormat"] = "objectArray" };
            if (skipToken is not null)
            {
                options["$skipToken"] = skipToken;
            }

            var answer = await SendAsync(
                HttpMethod.Post,
                ResourceGraphAddress,
                new JsonObject { ["subscriptions"] = new JsonArray(parts[1]), ["query"] = query, ["options"] = options },
                cancellationToken);
            alerts.AddRange((answer["data"] as JsonArray ?? []).OfType<JsonObject>().Select(MetricAlert.From));
            skipToken = answer["$skipToken"]?.GetValue<string>();
        }
        while (skipToken is not null);

        return alerts;
    }

    // Every queue, and every subscription of every topic, that a Logic App reads: the exempt subscriptions are left out.
    private async Task<List<string>> ReadEntitiesAsync(string namespaceId, CancellationToken cancellationToken)
    {
        var address = $"{Management}{namespaceId}";
        var entities = new List<string>();
        foreach (var queue in await ListAsync($"{address}/queues?{ServiceBusApiVersion}", cancellationToken))
        {
            entities.Add(Name(queue));
        }

        foreach (var topic in await ListAsync($"{address}/topics?{ServiceBusApiVersion}", cancellationToken))
        {
            var topicName = Name(topic);
            foreach (var subscription in await ListAsync($"{address}/topics/{Uri.EscapeDataString(topicName)}/subscriptions?{ServiceBusApiVersion}", cancellationToken))
            {
                if (!Exemptions.Subscriptions.Contains($"{topicName}/{Name(subscription)}"))
                {
                    entities.Add(Name(subscription));
                }
            }
        }

        return entities;
    }

    private static string Name(JsonNode resource) => resource["name"]?.GetValue<string>() ?? throw new InvalidOperationException("A Service Bus entity has no name.");

    private async Task<List<JsonNode>> ListAsync(string address, CancellationToken cancellationToken)
    {
        var items = new List<JsonNode>();
        string? page = address;
        while (page is not null)
        {
            var answer = await SendAsync(HttpMethod.Get, page, null, cancellationToken);
            items.AddRange(answer["value"] is JsonArray values ? values.OfType<JsonNode>() : []);
            page = answer["nextLink"]?.GetValue<string>();
        }

        return items;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string address, JsonNode? body, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScopes), cancellationToken);
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await clients.CreateClient(ManagementClient).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw new InvalidOperationException("Azure answered with nothing.");
    }

    /// <summary>
    /// One metric alert rule: where it is scoped, whether it is on, and what its dead-letter criteria watch. A criterion with an
    /// <c>EntityName</c> dimension watches the entities it includes (or every entity but the ones it excludes); one without watches them all.
    /// </summary>
    private sealed record MetricAlert(bool Enabled, IReadOnlyList<string> Scopes, IReadOnlyList<Func<string, bool>> DeadLetterCriteria)
    {
        public bool Watches(string entity) => DeadLetterCriteria.Any(criterion => criterion(entity));

        public static MetricAlert From(JsonObject row)
        {
            var scopes = (row["scopes"] as JsonArray ?? []).Select(scope => scope?.GetValue<string>()).OfType<string>().ToList();
            var criteria = new List<Func<string, bool>>();
            foreach (var criterion in (row["criteria"]?["allOf"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (!string.Equals(criterion["metricName"]?.GetValue<string>(), DeadLetterMetric, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dimension = (criterion["dimensions"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(dimension =>
                    string.Equals(dimension["name"]?.GetValue<string>(), EntityDimension, StringComparison.OrdinalIgnoreCase));
                if (dimension is null)
                {
                    criteria.Add(_ => true);
                    continue;
                }

                var values = (dimension["values"] as JsonArray ?? []).Select(value => value?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                var includes = string.Equals(dimension["operator"]?.GetValue<string>(), "Include", StringComparison.OrdinalIgnoreCase);
                criteria.Add(entity => includes ? values.Contains(entity) || values.Contains("*") : !values.Contains(entity));
            }

            return new MetricAlert(row["enabled"]?.GetValue<bool>() ?? false, scopes, criteria);
        }
    }

    // The subscriptions the estate leaves without an alert on purpose, from the file alerts.bicep's drift guard reads, as "topic/subscription".
    private static class Exemptions
    {
        public static readonly HashSet<string> Subscriptions = Load();

        private static HashSet<string> Load()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("estate/alert-exemptions.json")
                ?? throw new InvalidOperationException("The alert exemptions are not embedded.");
            var file = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            return [.. (file?["serviceBusEntities"] as JsonObject ?? []).Select(entry => entry.Key)];
        }
    }
}
