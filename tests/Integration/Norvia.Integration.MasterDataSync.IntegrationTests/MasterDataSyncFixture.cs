using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Norvia.AppHost;
using Norvia.Integration.MasterDataSync.IntegrationTests;

[assembly: AssemblyFixture(typeof(MasterDataSyncFixture))]

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>What the ERP API answered to a save.</summary>
public sealed record SaveAnswer(HttpStatusCode Status, JsonNode? Body, Uri? Location)
{
    /// <summary>The outcome the ERP names: Created, Updated, Unchanged or Repeat.</summary>
    public string? Outcome => Body?["outcome"]?.GetValue<string>();

    /// <summary>The stable code of a refusal.</summary>
    public string? Code => Body?["code"]?.GetValue<string>();
}

/// <summary>
/// Runs the Norvia AppHost once for the whole test run with what E3 master-data sync touches: SQL Server with the ERP's and the TMS's databases, the two Service Bus emulators, the ERP
/// (its API, and its UI whose outbox publishes the events), the TMS API and the master-data sync worker that plays the Logic App. The path is the real one, from an edit at the ERP API to the TMS's
/// copy. Only one hop is changed: the worker is told the TMS is a proxy, which forwards each call unless a test has scripted a fault for it, so the retries and the dead letters of the
/// worker can be shown. The fixture reads every event the ERP publishes through a subscription of its own beside the sync's, the way the Integration console's monitoring does.
/// The records the tests make have IDs of their own, so they share the estate and run one at a time.
/// </summary>
public sealed class MasterDataSyncFixture : TmsFixture
{
    public const string Topic = "master-data-events";
    public const string SyncSubscription = "master-data-sync";
    public const string MonitoringSubscription = "monitoring";
    public const string MasterDataManageRole = "MasterData.Manage";
    public const string MasterDataReplaceRole = "MasterData.Replace";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<ServiceBusReceivedMessage> _events = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _logs = new();
    private ServiceBusClient? _edge;
    private ServiceBusProcessor? _monitoring;
    private string _tmsConnection = string.Empty;
    private string _erpConnection = string.Empty;

    /// <summary>The proxy the worker reaches the TMS through.</summary>
    public TmsProxy Proxy { get; } = new();

    /// <summary>The ERP API as the ERP UI's identity calls it: holding the role that saves master data.</summary>
    public HttpClient Erp { get; private set; } = null!;

    /// <summary>The TMS API as the master-data sync Logic App's identity calls it: holding the role that replaces master data.</summary>
    public HttpClient TmsReplace { get; private set; } = null!;

    /// <summary>Every event of the master-data topic the monitoring subscription has seen.</summary>
    public IReadOnlyList<ServiceBusReceivedMessage> Events => [.. _events];

    public override async ValueTask InitializeAsync()
    {
        await Proxy.StartAsync();
        await base.InitializeAsync();
        Proxy.ForwardTo(App.GetEndpoint("tms-api", "http"));
        foreach (var resource in new[] { "erp-web", "erp-api", "master-data-sync-worker" })
        {
            await WaitForHealthyOrRunningAsync(resource);
        }

        WatchLogs();
        Erp = App.CreateHttpClient("erp-api");
        Erp.DefaultRequestHeaders.Authorization = new("Bearer", IssueToken([MasterDataManageRole], LocalTokens.ErpAudience, LocalTokens.ErpUiClientId));
        TmsReplace = CreateTmsClient(IssueToken([MasterDataReplaceRole], clientId: LocalTokens.MasterDataSyncLogicAppClientId));
        _tmsConnection = await ConnectionStringAsync("tms", CancellationToken.None);
        _erpConnection = await ConnectionStringAsync("erp", CancellationToken.None);

        _edge = new ServiceBusClient(await ConnectionStringAsync("messaging-2", CancellationToken.None));
        _monitoring = _edge.CreateProcessor(Topic, MonitoringSubscription);
        _monitoring.ProcessMessageAsync += args =>
        {
            _events.Enqueue(args.Message);
            return Task.CompletedTask;
        };
        _monitoring.ProcessErrorAsync += _ => Task.CompletedTask;
        await _monitoring.StartProcessingAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_monitoring is not null)
        {
            await _monitoring.DisposeAsync();
        }

        if (_edge is not null)
        {
            await _edge.DisposeAsync();
        }

        Erp?.Dispose();
        TmsReplace?.Dispose();
        await base.DisposeAsync();
        await Proxy.DisposeAsync();
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheMasterDataSyncTestsDontUse();

        // The worker's retry waits are the workflow's 5 s up to 20 s; a test waits for them, so they are short here. The count stays the workflow's three.
        var worker = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "master-data-sync-worker"));
        worker.WithEnvironment("MasterDataSync__TmsBaseAddress", Proxy.Address.ToString())
            .WithEnvironment("MasterDataSync__Retry__Interval", "00:00:00.200")
            .WithEnvironment("MasterDataSync__Retry__MaximumInterval", "00:00:00.800");
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "erp-web"))
            .WithEnvironment("Outbox__PollingInterval", "00:00:00.100")
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
    }

    /// <summary>The TMS keeps the repo's seed file the AppHost gives it, so the Rate Cards and the Fleet are the estate's, the same the ERP loads.</summary>
    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings.Remove("Seed__Path");
        return settings;
    }

    /// <summary>Keeps what the apps of the path log, so a test that times out can say what they said.</summary>
    private void WatchLogs()
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var loggers = App.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var name in new[] { "erp-web", "erp-api", "master-data-sync-worker", "tms-api" })
        {
            var resource = model.Resources.Single(candidate => candidate.Name == name);
            _ = Task.Run(async () =>
            {
                await foreach (var batch in loggers.WatchAsync(resource))
                {
                    var queue = _logs.GetOrAdd(name, _ => new());
                    foreach (var line in batch.Where(line => LogNoise.IsMatch(line.Content) is false && Signal.IsMatch(line.Content)))
                    {
                        queue.Enqueue(line.Content);
                        while (queue.Count > 150)
                        {
                            queue.TryDequeue(out _);
                        }
                    }
                }
            });
        }
    }

    private static readonly System.Text.RegularExpressions.Regex LogNoise = new(@"Executed DbCommand|Azure\.Messaging\.ServiceBus\[[0-9]+\]|Receiver: ReceiveBatch");
    private static readonly System.Text.RegularExpressions.Regex Signal = new(@"warn|fail|error|crit|exception|Published|dead|retry|TMS API|Created|Updated|Unauthorized|Forbidden", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private string LogTail() => string.Join(Environment.NewLine, _logs.Select(pair => $"--- {pair.Key}{Environment.NewLine}{string.Join(Environment.NewLine, pair.Value.TakeLast(25).Select(line => line.Length > 300 ? line[..300] : line))}"));

    private async Task WaitForHealthyOrRunningAsync(string resource)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.ResourceNotifications.WaitForResourceAsync(resource, KnownResourceStates.Running, timeout.Token);
    }

    /// <summary>Saves a record through the ERP API, as the ERP UI does for Finance.</summary>
    public async Task<SaveAnswer> SaveAsync(Kind kind, string id, JsonObject body, string? idempotencyKey = null, HttpClient? caller = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"{kind.Route}/{id}", UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await (caller ?? Erp).SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return new SaveAnswer(response.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text), response.Headers.Location);
    }

    /// <summary>A client of the ERP API holding the given roles, or no token.</summary>
    public HttpClient ErpAs(params string[]? roles)
    {
        var client = App.CreateHttpClient("erp-api");
        if (roles is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", IssueToken(roles, LocalTokens.ErpAudience, LocalTokens.ErpUiClientId));
        }

        return client;
    }

    /// <summary>Replaces a record in the TMS as the Logic App does: the body is the record but for its ID, with its revision.</summary>
    public async Task<(HttpStatusCode Status, JsonNode? Body)> ReplaceInTmsAsync(Kind kind, string id, JsonObject record, int revision, HttpClient? caller = null, CancellationToken ct = default)
    {
        var body = (JsonObject)record.DeepClone();
        body["revision"] = revision;
        using var response = await (caller ?? TmsReplace).PutAsJsonAsync(new Uri($"{kind.Route}/{id}", UriKind.Relative), body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
    }

    /// <summary>The events of the record that the ERP has published, oldest first, once the outbox has run a few cycles with none new.</summary>
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> EventsOfAsync(Kind kind, string id, CancellationToken ct)
    {
        var count = -1;
        while (count != _events.Count)
        {
            count = _events.Count;
            await Task.Delay(Settle, ct);
        }

        return [.. _events.Where(message => message.Subject == kind.EventType && RecordOf(kind, message)?["id"]?.GetValue<string>() == id)];
    }

    /// <summary>Waits for the event of the record at the revision to reach the monitoring subscription, and returns it.</summary>
    public async Task<ServiceBusReceivedMessage> EventAsync(Kind kind, string id, int revision, CancellationToken ct) =>
        await WaitForAsync(
            () => Task.FromResult(_events.FirstOrDefault(message => message.Subject == kind.EventType
                && RecordOf(kind, message) is { } record && record["id"]?.GetValue<string>() == id && record["revision"]?.GetValue<int>() == revision)),
            $"the {kind.EventType} event of {id} at revision {revision}", ct);

    /// <summary>The record an event carries.</summary>
    public static JsonNode? RecordOf(Kind kind, ServiceBusReceivedMessage message)
    {
        try
        {
            return JsonNode.Parse(message.Body.ToString())?[kind.RecordName];
        }
        catch (System.Text.Json.JsonException)
        {
            // A message the tests sent to show what the worker does with one it can't read.
            return null;
        }
    }

    /// <summary>The TMS's copy of a record as its row holds it, once it is at the revision or later; fails the test when it never gets there.</summary>
    public async Task<IReadOnlyDictionary<string, object?>> ReplicaAsync(Kind kind, string id, int revision, CancellationToken ct) =>
        await WaitForAsync(
            async () => await ReplicaRowAsync(kind, id, ct) is { } row && (int)row["Revision"]! >= revision ? row : null,
            $"the TMS's copy of {id} at revision {revision}", ct);

    /// <summary>The TMS's copy of a record as its row holds it, or null when the TMS has replicated none.</summary>
    public async Task<IReadOnlyDictionary<string, object?>?> ReplicaRowAsync(Kind kind, string id, CancellationToken ct) =>
        (await RowsAsync(_tmsConnection, $"SELECT * FROM {kind.ReplicaTable} WHERE Id = @id", ct, ("@id", id))).SingleOrDefault();

    /// <summary>Rows of one of the two databases, each column by name.</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TmsRowsAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters) =>
        RowsAsync(_tmsConnection, sql, ct, parameters);

    /// <inheritdoc cref="TmsRowsAsync"/>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ErpRowsAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters) =>
        RowsAsync(_erpConnection, sql, ct, parameters);

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(string connectionString, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).ToDictionary(index => reader.GetName(index), index => reader.IsDBNull(index) ? null : reader.GetValue(index)));
        }

        return rows;
    }

    /// <summary>Sends a message to the topic as the ERP's outbox would, to show what the subscriptions and the worker do with a message the ERP never sent.</summary>
    public async Task SendToTopicAsync(string eventType, string body, string messageId, string correlationId, CancellationToken ct)
    {
        await using var sender = _edge!.CreateSender(Topic);
        var message = new ServiceBusMessage(BinaryData.FromString(body))
        {
            MessageId = messageId,
            Subject = eventType,
            CorrelationId = correlationId,
            ContentType = "application/json",
        };
        message.ApplicationProperties["eventType"] = eventType;
        message.ApplicationProperties["correlationId"] = correlationId;
        message.ApplicationProperties["flow"] = "master-data";
        await sender.SendMessageAsync(message, ct);
    }

    /// <summary>The message a Service Bus subscription of the topic holds as dead-lettered, once it is there; fails the test when it never is.</summary>
    public async Task<ServiceBusReceivedMessage> DeadLetteredAsync(string subscription, string messageId, CancellationToken ct) =>
        await WaitForAsync(async () => (await DeadLetterQueueAsync(subscription, ct)).FirstOrDefault(message => message.MessageId == messageId), $"message {messageId} in the dead-letter queue of {subscription}", ct);

    /// <summary>What the subscription's dead-letter queue holds.</summary>
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> DeadLetterQueueAsync(string subscription, CancellationToken ct)
    {
        await using var receiver = _edge!.CreateReceiver(Topic, subscription, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        return await receiver.PeekMessagesAsync(500, cancellationToken: ct);
    }

    /// <summary>Waits until the probe finds something, polling; fails the test with what it waited for when it doesn't in a minute.</summary>
    public async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, string what, CancellationToken ct)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WaitTimeout);
        try
        {
            while (true)
            {
                if (await probe() is { } found)
                {
                    return found;
                }

                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var outbox = await RowsAsync(_erpConnection, "SELECT TOP 5 EventType, Topic, PublishedAt, CreatedAt FROM OutboxMessages ORDER BY Id DESC", CancellationToken.None);
            throw new TimeoutException($"Waited {WaitTimeout.TotalSeconds:0} s for {what}.{Environment.NewLine}Outbox: {string.Join("; ", outbox.Select(row => string.Join(',', row.Values)))}{Environment.NewLine}{LogTail()}");
        }
    }

    /// <summary>Gives the sync time to do what it would, so a message it should not have touched shows. A test uses it only after a positive control has proved the path works.</summary>
    public static Task SettleAsync(CancellationToken ct) => Task.Delay(Settle, ct);
}
