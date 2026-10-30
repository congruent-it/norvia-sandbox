using System.Net.Http.Headers;
using System.Text.RegularExpressions;
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
///
/// The other facts of the checklist (#393) come the same way, as Reader: the Logic Apps, the action group, the retention, the TMS app's
/// settings and the storage accounts from Resource Graph, and a storage account's lifecycle rules, the Defender setting and API Management's
/// policies from their resource. What Reader cannot see is left null, and a Check gives no Finding for it: the app settings of a Logic App
/// (a Carrier endpoint secret kept inline), the workflows' definitions (tracked properties, the report back to the Driver app) and the
/// agents' guards. The docs name them.
/// </summary>
internal sealed partial class AzureEstateConfiguration(
    IHttpClientFactory clients,
    TokenCredential credential,
    IOptions<AzureEstateOptions> options,
    TimeProvider time) : IEstateConfiguration
{
    public const string ManagementClient = "estate-management";

    /// <summary>The estate's name in the facts: it is the real one.</summary>
    public const string EstateName = "azure";

    private const string Management = "https://management.azure.com";
    private const string ServiceBusApiVersion = "api-version=2024-01-01";
    private const string ResourceGraphAddress = $"{Management}/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";
    private const string DeadLetterMetric = "DeadletteredMessages";
    private const string FailedRunsMetric = "WorkflowRunsCompleted";
    private const string MetricsApiVersion = "api-version=2018-01-01";
    private const string StorageApiVersion = "api-version=2024-01-01";
    private const string DefenderApiVersion = "api-version=2022-12-01-preview";
    private const string ApiManagementApiVersion = "api-version=2024-05-01";
    private const string InboundStoragePrefix = "stinbound";
    private const string TransitStoragePrefix = "stdriver";
    private const string ProofsContainer = "proofs-of-delivery";
    private const string TmsApp = "tms-api";
    private const string PartnerApi = "norvia-partner-api";
    private static readonly string[] ShipperTrackingOperations = ["list-shipper-transport-orders", "find-shipper-transport-order", "get-shipper-transport-order"];
    private const string EntityDimension = "EntityName";

    // The reads inside one step do not depend on each other, so they run together, this many at a time (Azure Resource Manager throttles per subscription).
    private const int Concurrency = 8;
    internal static readonly string[] ManagementScopes = [$"{Management}/.default"];

    public async Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        var settings = options.Value;
        var namespaceId = $"{settings.ResourceGroupId.TrimEnd('/')}/providers/Microsoft.ServiceBus/namespaces/{settings.ServiceBusNamespace}";
        var group = new Group(settings.ResourceGroupId.Split('/', StringSplitOptions.RemoveEmptyEntries));

        var graph = StartGraphReads(group, cancellationToken);
        try
        {
            return await ReadStepsAsync(graph, group, namespaceId, progress, cancellationToken);
        }
        finally
        {
            await graph.SettledAsync();
        }
    }

    private async Task<EstateConfiguration> ReadStepsAsync(GraphReads graph, Group group, string namespaceId, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Reading the alert rules");
        var alerts = (await graph.Alerts).Select(MetricAlert.From).ToList();
        var watching = alerts.Where(alert => alert.Enabled && alert.Scopes.Contains(namespaceId, StringComparer.OrdinalIgnoreCase)).ToList();

        progress?.Report("Reading the Service Bus queues and subscriptions");
        var entities = (await ReadEntitiesAsync(namespaceId, cancellationToken))
            .DistinctBy(entity => entity.Name, StringComparer.Ordinal)
            .Select(entity => new ServiceBusEntity(entity.Name, watching.Exists(alert => alert.Watches(entity.Name)), entity.DeadLetteredMessages))
            .ToList();

        progress?.Report("Reading the Logic Apps");
        var logicApps = await graph.LogicApps;
        progress?.Report("Reading the TMS API's settings");
        var tms = ReadTmsSettings(await graph.TmsApp);
        progress?.Report("Reading API Management's policies");
        var apiManagement = await ReadApiManagementAsync(await graph.ApiManagement, cancellationToken);
        progress?.Report("Reading how long Application Insights and its workspace keep what they trace");
        var retention = ReadRetention(await graph.Components, await graph.Workspaces);
        progress?.Report("Reading the storage accounts' lifecycle rules and malware scanning");
        var (inbound, transit, proofs) = await ReadStorageAsync(await graph.StorageAccounts, cancellationToken);

        progress?.Report("Counting the failed runs of each Logic App");
        var flows = await ReadAllAsync(logicApps, async app =>
        {
            var id = app["id"]?.GetValue<string>() ?? string.Empty;
            return new LogicAppFlow(
                app["name"]?.GetValue<string>() ?? string.Empty,
                alerts.Exists(alert => alert.Enabled && alert.WatchesFailedRuns && alert.Scopes.Contains(id, StringComparer.OrdinalIgnoreCase)),
                RecordsTrackedProperties: null,
                FailedRuns: await ReadFailedRunsAsync(id, cancellationToken));
        }, cancellationToken);

        progress?.Report("Reading the alert receivers");
        return new EstateConfiguration(EstateName, entities)
        {
            Flows = flows,
            AlertReceivers = ReadAlertReceivers(await graph.ActionGroups),
            Storage = new StorageFacts(
                inbound.HasLifecycleRule,
                transit.HasLifecycleRule,
                proofs,
                inbound.MalwareScanning,
                inbound.Account,
                transit.Account),
            InlinedCarrierSecrets = null,
            Retention = retention,
            BusinessSafetyNets = new BusinessSafetyNetFacts(
                tms.Setting("ShipmentStale__ThresholdsSet"),
                tms.Setting("ProofsOfDelivery__MissingThresholdSet"),
                tms.Setting("LateDelivery__CheckEnabled")),
            ApiManagement = apiManagement,
            DriverReportsReportBack = null,
            ConversationSweeperRuns = logicApps
                .Where(app => string.Equals(app["workload"]?.GetValue<string>(), "conversation-sweeper", StringComparison.Ordinal))
                .Select(app => Flag(app["enabled"]))
                .FirstOrDefault(),
            Agents = null,
        };
    }

    // What the steps read from Resource Graph. The queries do not depend on each other or on the other reads, so they all start at once, and a step
    // waits for its own: the steps still report in their order, and a query is done by the time its step asks.
    private sealed record GraphReads(
        Task<List<JsonObject>> Alerts,
        Task<List<JsonObject>> LogicApps,
        Task<List<JsonObject>> TmsApp,
        Task<List<JsonObject>> ApiManagement,
        Task<List<JsonObject>> Components,
        Task<List<JsonObject>> Workspaces,
        Task<List<JsonObject>> StorageAccounts,
        Task<List<JsonObject>> ActionGroups)
    {
        /// <summary>Waits for every query to end, so one that fails after an earlier step did is not left unobserved. What it answers is only the failure, which a step has already met.</summary>
        public Task<AggregateException?> SettledAsync() =>
            Task.WhenAll(Alerts, LogicApps, TmsApp, ApiManagement, Components, Workspaces, StorageAccounts, ActionGroups)
                .ContinueWith(static all => all.Exception, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private GraphReads StartGraphReads(Group group, CancellationToken cancellationToken) => new(
        QueryAsync(group, "microsoft.insights/metricalerts", "| project name, enabled = tobool(properties.enabled), scopes = properties.scopes, criteria = properties.criteria", cancellationToken),
        QueryAsync(group, "microsoft.web/sites", "| where kind contains 'workflowapp' | project id, name, enabled = tobool(properties.enabled), workload = tostring(tags.workload)", cancellationToken),
        QueryAsync(group, "microsoft.app/containerapps", $"| where name =~ '{TmsApp}' | project env = properties.template.containers[0].env", cancellationToken),
        QueryAsync(group, "microsoft.apimanagement/service", "| project id", cancellationToken),
        QueryAsync(group, "microsoft.insights/components", "| project days = toint(properties.RetentionInDays)", cancellationToken),
        QueryAsync(group, "microsoft.operationalinsights/workspaces", "| project days = toint(properties.retentionInDays)", cancellationToken),
        QueryAsync(group, "microsoft.storage/storageaccounts", "| project id, name", cancellationToken),
        QueryAsync(
            group,
            "microsoft.insights/actiongroups",
            "| project enabled = tobool(properties.enabled), receivers = coalesce(array_length(properties.emailReceivers), 0) + coalesce(array_length(properties.smsReceivers), 0) + coalesce(array_length(properties.webhookReceivers), 0) + coalesce(array_length(properties.armRoleReceivers), 0)",
            cancellationToken));

    // Every row of a Resource Graph query over the resources of one type in the estate's resource group, across pages.
    private async Task<List<JsonObject>> QueryAsync(Group group, string type, string projection, CancellationToken cancellationToken)
    {
        var query = $"resources | where type =~ '{type}' | where resourceGroup =~ '{group.Name.Replace("'", "\\'", StringComparison.Ordinal)}' {projection}";
        var rows = new List<JsonObject>();
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
                new JsonObject { ["subscriptions"] = new JsonArray(group.Subscription), ["query"] = query, ["options"] = options },
                cancellationToken) ?? throw new InvalidOperationException("Resource Graph answered with nothing.");
            rows.AddRange((answer["data"] as JsonArray ?? []).OfType<JsonObject>());
            skipToken = answer["$skipToken"]?.GetValue<string>();
        }
        while (skipToken is not null);

        return rows;
    }

    // The receivers of the estate's action groups that are on. Null when the estate has no action group, so there is nothing to say.
    private static int? ReadAlertReceivers(List<JsonObject> groups) =>
        groups.Count == 0
            ? null
            : groups.Where(row => Flag(row["enabled"]) ?? true).Sum(row => row["receivers"]?.GetValue<int>() ?? 0);

    // What Application Insights keeps against what the workspace, and so the run history, keeps. Null when either is not there.
    private static RetentionFacts? ReadRetention(List<JsonObject> components, List<JsonObject> workspaces) =>
        components.Count == 0 || workspaces.Count == 0
            ? null
            : new RetentionFacts(
                components.Min(row => row["days"]?.GetValue<int>() ?? 0),
                workspaces.Max(row => row["days"]?.GetValue<int>() ?? 0));

    // The TMS API's Container App and the settings it runs with. Where the app is not there, nothing is known of them.
    private static AppSettings ReadTmsSettings(List<JsonObject> apps)
    {
        var env = (apps.FirstOrDefault()?["env"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(item => item["name"] is not null && item["value"] is not null)
            .ToDictionary(item => item["name"]!.GetValue<string>(), item => item["value"]!.GetValue<string>(), StringComparer.Ordinal);
        return new AppSettings(env);
    }

    // API Management's policies on the Shipper tracking operations: the longest an answer is cached, and whether all of them limit the rate.
    private async Task<ApiManagementFacts> ReadApiManagementAsync(List<JsonObject> services, CancellationToken cancellationToken)
    {
        if (services.Count == 0)
        {
            return new ApiManagementFacts(null, null);
        }

        var serviceId = services[0]["id"]?.GetValue<string>() ?? throw new InvalidOperationException("The API Management service has no ID.");
        var policies = await ReadAllAsync(ShipperTrackingOperations, async operation =>
        {
            var policy = await TrySendAsync(HttpMethod.Get, $"{Management}{serviceId}/apis/{PartnerApi}/operations/{operation}/policies/policy?{ApiManagementApiVersion}&format=rawxml", null, notFoundIsAnAnswer: true, cancellationToken);
            return policy?["properties"]?["value"]?.GetValue<string>() ?? string.Empty;
        }, cancellationToken);

        return new ApiManagementFacts(
            policies.Max(CacheSeconds),
            policies.All(policy => policy.Contains("<rate-limit", StringComparison.Ordinal)));
    }

    private static int CacheSeconds(string policy) =>
        CacheDuration().Matches(policy).Select(match => int.Parse(match.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"<cache-store(?:-value)?\b[^>]*\bduration=""(?<seconds>\d+)""")]
    private static partial Regex CacheDuration();

    // The inbound and the transit storage accounts' lifecycle rules, the inbound account's malware scanning, and whether the TMS storage's
    // Proof of delivery files move to cool. A protection is null where the account that should hold it is not there.
    private async Task<(Protections Inbound, Protections Transit, bool? ProofsMoveToCool)> ReadStorageAsync(List<JsonObject> storageAccounts, CancellationToken cancellationToken)
    {
        var accounts = storageAccounts
            .Select(row => (Id: row["id"]?.GetValue<string>() ?? string.Empty, Name: row["name"]?.GetValue<string>() ?? string.Empty))
            .ToList();

        var inbound = accounts.Find(account => account.Name.StartsWith(InboundStoragePrefix, StringComparison.Ordinal));
        var transit = accounts.Find(account => account.Name.StartsWith(TransitStoragePrefix, StringComparison.Ordinal));
        var tms = accounts.Where(account => account != inbound && account != transit).ToList();

        var proofs = ProofsMoveToCoolAsync(tms, cancellationToken);
        var inboundProtections = inbound.Id is null ? Task.FromResult(default(Protections)) : InboundProtectionsAsync(inbound, cancellationToken);
        var transitProtections = transit.Id is null ? Task.FromResult(default(Protections)) : TransitProtectionsAsync(transit, cancellationToken);
        await Task.WhenAll(proofs, inboundProtections, transitProtections);

        return (await inboundProtections, await transitProtections, await proofs);
    }

    private async Task<bool?> ProofsMoveToCoolAsync(List<(string Id, string Name)> accounts, CancellationToken cancellationToken) =>
        accounts.Count == 0
            ? null
            : (await ReadAllAsync(accounts, account => ReadRulesAsync(account.Id, cancellationToken), cancellationToken))
                .Any(rules => rules.Exists(rule => rule.Prefixes.Exists(prefix => prefix.StartsWith(ProofsContainer, StringComparison.Ordinal)) && rule.MovesToCool));

    private async Task<Protections> InboundProtectionsAsync((string Id, string Name) account, CancellationToken cancellationToken)
    {
        var rules = ReadRulesAsync(account.Id, cancellationToken);
        var scanning = MalwareScanningAsync(account.Id, cancellationToken);
        await Task.WhenAll(rules, scanning);
        return new Protections((await rules).Exists(rule => rule.Deletes), await scanning, account.Name);
    }

    private async Task<Protections> TransitProtectionsAsync((string Id, string Name) account, CancellationToken cancellationToken) =>
        new((await ReadRulesAsync(account.Id, cancellationToken)).Exists(rule => rule.Deletes), null, account.Name);

    private async Task<bool> MalwareScanningAsync(string storageId, CancellationToken cancellationToken)
    {
        var setting = await TrySendAsync(HttpMethod.Get, $"{Management}{storageId}/providers/Microsoft.Security/defenderForStorageSettings/current?{DefenderApiVersion}", null, notFoundIsAnAnswer: true, cancellationToken);
        var properties = setting?["properties"];
        return (properties?["isEnabled"]?.GetValue<bool>() ?? false) && (properties?["malwareScanning"]?["onUpload"]?["isEnabled"]?.GetValue<bool>() ?? false);
    }

    // The enabled lifecycle rules of a storage account. An account with no policy has none.
    private async Task<List<LifecycleRule>> ReadRulesAsync(string storageId, CancellationToken cancellationToken)
    {
        var policy = await TrySendAsync(HttpMethod.Get, $"{Management}{storageId}/managementPolicies/default?{StorageApiVersion}", null, notFoundIsAnAnswer: true, cancellationToken);
        return
        [
            .. (policy?["properties"]?["policy"]?["rules"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(rule => rule["enabled"]?.GetValue<bool>() ?? true)
                .Select(rule =>
                {
                    var baseBlob = rule["definition"]?["actions"]?["baseBlob"];
                    return new LifecycleRule(
                        [.. (rule["definition"]?["filters"]?["prefixMatch"] as JsonArray ?? []).Select(prefix => prefix?.GetValue<string>()).OfType<string>()],
                        baseBlob?["delete"] is not null,
                        baseBlob?["tierToCool"] is not null);
                }),
        ];
    }

    // How many runs of a Logic App failed in the failure window, or null when Azure Monitor has no such metric for it, from Azure Monitor's WorkflowRunsCompleted metric (the one the failed-run alert watches).
    private async Task<int?> ReadFailedRunsAsync(string logicAppId, CancellationToken cancellationToken)
    {
        var end = time.GetUtcNow();
        var start = end.AddDays(-EstateConfiguration.FailureWindowDays);
        var address = $"{Management}{logicAppId}/providers/microsoft.insights/metrics?{MetricsApiVersion}&metricnames={FailedRunsMetric}&aggregation=Total"
            + $"&timespan={Uri.EscapeDataString($"{start:yyyy-MM-ddTHH:mm:ssZ}/{end:yyyy-MM-ddTHH:mm:ssZ}")}&interval=P1D&$filter={Uri.EscapeDataString("status eq 'Failed'")}";
        var answer = await TrySendAsync(HttpMethod.Get, address, null, notFoundIsAnAnswer: true, cancellationToken);
        if (answer is null)
        {
            return null;
        }

        var total = (answer["value"] as JsonArray ?? [])
            .SelectMany(metric => metric?["timeseries"] as JsonArray ?? [])
            .SelectMany(series => series?["data"] as JsonArray ?? [])
            .Sum(point => point?["total"]?.GetValue<double>() ?? 0);
        return (int)Math.Round(total);
    }

    // Every queue, and every subscription of every topic, that a Logic App reads, with the dead-lettered messages it holds: the exempt subscriptions are left out.
    private async Task<List<(string Name, int? DeadLetteredMessages)>> ReadEntitiesAsync(string namespaceId, CancellationToken cancellationToken)
    {
        var address = $"{Management}{namespaceId}";
        var entities = new List<(string, int?)>();
        foreach (var queue in await ListAsync($"{address}/queues?{ServiceBusApiVersion}", cancellationToken))
        {
            entities.Add((Name(queue), DeadLettered(queue)));
        }

        var topics = await ListAsync($"{address}/topics?{ServiceBusApiVersion}", cancellationToken);
        var subscriptions = await ReadAllAsync(topics, async topic =>
        {
            var topicName = Name(topic);
            return (await ListAsync($"{address}/topics/{Uri.EscapeDataString(topicName)}/subscriptions?{ServiceBusApiVersion}", cancellationToken))
                .Where(subscription => !Exemptions.Subscriptions.Contains($"{topicName}/{Name(subscription)}"))
                .Select(subscription => (Name(subscription), DeadLettered(subscription)))
                .ToList();
        }, cancellationToken);
        entities.AddRange(subscriptions.SelectMany(found => found));

        return entities;
    }

    private static int? DeadLettered(JsonNode entity) => entity["properties"]?["countDetails"]?["deadLetterMessageCount"]?.GetValue<int>();

    private static string Name(JsonNode resource) => resource["name"]?.GetValue<string>() ?? throw new InvalidOperationException("A Service Bus entity has no name.");

    // The answers of one read per item, in the items' order, with at most Concurrency reads under way.
    private static async Task<TResult[]> ReadAllAsync<TItem, TResult>(IEnumerable<TItem> items, Func<TItem, Task<TResult>> read, CancellationToken cancellationToken)
    {
        using var limit = new SemaphoreSlim(Concurrency);
        return await Task.WhenAll(items.Select(async item =>
        {
            await limit.WaitAsync(cancellationToken);
            try
            {
                return await read(item);
            }
            finally
            {
                limit.Release();
            }
        }));
    }

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

    private async Task<JsonNode> SendAsync(HttpMethod method, string address, JsonNode? body, CancellationToken cancellationToken) =>
        await TrySendAsync(method, address, body, notFoundIsAnAnswer: false, cancellationToken) ?? throw new InvalidOperationException("Azure answered with nothing.");

    // A resource that is not there answers 404; where that is an answer (no lifecycle policy, no Defender setting), it is null.
    private async Task<JsonNode?> TrySendAsync(HttpMethod method, string address, JsonNode? body, bool notFoundIsAnAnswer, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScopes), cancellationToken);
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await clients.CreateClient(ManagementClient).SendAsync(request, cancellationToken);
        if (notFoundIsAnAnswer && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw new InvalidOperationException("Azure answered with nothing.");
    }

    /// <summary>
    /// One metric alert rule: where it is scoped, whether it is on, and what its dead-letter criteria watch. A criterion with an
    /// <c>EntityName</c> dimension watches the entities it includes (or every entity but the ones it excludes); one without watches them all.
    /// </summary>
    private sealed record MetricAlert(bool Enabled, IReadOnlyList<string> Scopes, IReadOnlyList<Func<string, bool>> DeadLetterCriteria, bool WatchesFailedRuns)
    {
        public bool Watches(string entity) => DeadLetterCriteria.Any(criterion => criterion(entity));

        public static MetricAlert From(JsonObject row)
        {
            var scopes = (row["scopes"] as JsonArray ?? []).Select(scope => scope?.GetValue<string>()).OfType<string>().ToList();
            var criteria = new List<Func<string, bool>>();
            var watchesFailedRuns = false;
            foreach (var criterion in (row["criteria"]?["allOf"] as JsonArray ?? []).OfType<JsonObject>())
            {
                watchesFailedRuns |= string.Equals(criterion["metricName"]?.GetValue<string>(), FailedRunsMetric, StringComparison.OrdinalIgnoreCase);
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

            return new MetricAlert(Flag(row["enabled"]) ?? false, scopes, criteria, watchesFailedRuns);
        }
    }

    // Resource Graph answers a tobool(...) column as a number, 1 or 0, and not as a JSON boolean.
    private static bool? Flag(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => node.GetValue<double>() != 0,
        _ => null,
    };

    private sealed record Group(string[] Parts)
    {
        public string Subscription => Parts[1];

        public string Name => Parts[3];
    }

    private sealed record AppSettings(Dictionary<string, string> Env)
    {
        // A setting the app runs with, or null where the app does not carry it.
        public bool? Setting(string name) => Env.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private readonly record struct Protections(bool? HasLifecycleRule, bool? MalwareScanning, string? Account = null);

    private sealed record LifecycleRule(List<string> Prefixes, bool Deletes, bool MovesToCool);

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
