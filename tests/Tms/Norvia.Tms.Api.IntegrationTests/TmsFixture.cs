using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.Tms.Api.IntegrationTests.TmsFixture))]

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run (TMS, SQL and the Service Bus emulator) and records every
/// message published to the Transport Order and Shipment events topics through subscriptions of its own. It holds the key the TMS
/// trusts locally, so it issues tokens the way Entra would: <see cref="Tms"/> calls as the intake Logic App does.
/// The TMS writes its logs as JSON with their scopes, which the fixture keeps so tests can follow a Message trail.
/// The TMS reads a seed file of the tests' own, so the Rate Cards it rates against are the tests', not the demo's.
/// Its rating worker sweeps only once an hour, so a Transport Order rated within moments was rated by its acceptance's
/// trigger.
/// </summary>
public class TmsFixture : IAsyncLifetime
{
    public const string TransportOrderEventsTopic = "transport-order-events";
    public const string ShipmentEventsTopic = "shipment-events";
    private const string TestSubscription = "tms-api-integration-tests";
    public const string IntakeRole = "TransportOrders.Intake";

    /// <summary>The app role only the TMS UI's managed identity holds: Customer service's re-rate goes through it.</summary>
    public const string ManageRole = "TransportOrders.Manage";

    /// <summary>The app role only the Carrier-answer Logic App's managed identity holds (P4).</summary>
    public const string AnswerRole = "CarrierBookings.Answer";

    /// <summary>The app role only the P4 booking Logic App's managed identity holds, for its sent and not-delivered reports.</summary>
    public const string ReportRole = "CarrierBookings.Report";

    /// <summary>The client ID the TMS takes as the WMS handoff's caller. No WMS exists yet, so only the tests call as it.</summary>
    public const string WmsClientId = "norvia-test-wms";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private readonly ConcurrentQueue<ServiceBusReceivedMessage> _published = new();
    private readonly ConcurrentQueue<TmsLog> _tmsLogs = new();
    private DistributedApplication? _app;
    private ServiceBusClient? _serviceBus;
    private readonly List<ServiceBusProcessor> _processors = [];

    /// <summary>A client holding the intake role, as the intake Logic App's managed identity does.</summary>
    public HttpClient Tms { get; private set; } = null!;

    /// <summary>A client holding the manage role, as the TMS UI's managed identity does.</summary>
    public HttpClient TmsUi { get; private set; } = null!;

    /// <summary>A client holding the answer role, as the Carrier-answer Logic App's managed identity will (P4, later ticket).</summary>
    public HttpClient CarrierAnswer { get; private set; } = null!;

    /// <summary>A client holding the report role, as the P4 booking Logic App's managed identity will (P4, later ticket).</summary>
    public HttpClient CarrierReport { get; private set; } = null!;

    public IEnumerable<ServiceBusReceivedMessage> Published => _published;

    /// <summary>Every log the TMS has written so far.</summary>
    public IEnumerable<TmsLog> TmsLogs => _tmsLogs;

    /// <summary>The links the Aspire dashboard shows for the TMS API.</summary>
    public IReadOnlyList<Uri> TmsDashboardUrls =>
        _app!.ResourceNotifications.TryGetCurrentState("tms-api", out var state)
            ? [.. state.Snapshot.Urls.Select(url => new Uri(url.Url))]
            : [];

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"],
            startup.Token);
        foreach (var topicName in new[] { TransportOrderEventsTopic, ShipmentEventsTopic })
        {
            var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Single(t => t.TopicName == topicName);
            builder.CreateResourceBuilder(topic).AddServiceBusSubscription($"{topicName}-{TestSubscription}", TestSubscription);
        }

        var tmsApi = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "tms-api"))
            // Aspire sets the formatter under this exact name, and environment names are case-sensitive on Linux.
            .WithEnvironment("LOGGING__CONSOLE__FORMATTERNAME", "json")
            .WithEnvironment("Logging__Console__FormatterOptions__IncludeScopes", "true");
        foreach (var (name, value) in TmsApiSettings())
        {
            tmsApi.WithEnvironment(name, value);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        CollectTmsLogs();
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", startup.Token);

        Tms = CreateTmsClient(IssueToken([IntakeRole]));
        TmsUi = CreateTmsClient(IssueToken([ManageRole], clientId: LocalTokens.TmsUiClientId));
        CarrierAnswer = CreateTmsClient(IssueToken([AnswerRole], clientId: "norvia-test-carrier-answer-logic-app"));
        CarrierReport = CreateTmsClient(IssueToken([ReportRole], clientId: "norvia-test-p4-booking-logic-app"));

        _serviceBus = new ServiceBusClient(await _app.GetConnectionStringAsync("messaging", startup.Token));
        foreach (var topicName in new[] { TransportOrderEventsTopic, ShipmentEventsTopic })
        {
            var processor = _serviceBus.CreateProcessor(topicName, TestSubscription);
            processor.ProcessMessageAsync += args =>
            {
                _published.Enqueue(args.Message);
                return Task.CompletedTask;
            };
            processor.ProcessErrorAsync += _ => Task.CompletedTask;
            _processors.Add(processor);
            await processor.StartProcessingAsync(startup.Token);
        }
    }

    /// <summary>What the fixture sets on the TMS API on top of what the AppHost gives it, as environment variables.</summary>
    protected virtual Dictionary<string, string> TmsApiSettings() => new()
    {
        ["Seed__Path"] = Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json"),
        ["IntakeCallers__ClientIdsByChannel__Wms__0"] = WmsClientId,
        ["Rating__SweepInterval"] = "01:00:00",

        // A test run has no dashboard to take the TMS's telemetry, and a TMS that stops keeps trying to flush it there
        // for seconds, too long for a restart. Tests read the TMS's console logs instead.
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
    };

    /// <summary>Waits for the TMS to write a log that matches, as logs arrive a moment after the call.</summary>
    public async Task<TmsLog> TmsLogAsync(Func<TmsLog, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!_tmsLogs.Any(match))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }

        return _tmsLogs.First(match);
    }

    /// <summary>Waits for one of the AppHost's resources to report healthy.</summary>
    public async Task WaitForHealthyAsync(string resourceName, CancellationToken cancellationToken) =>
        await _app!.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);

    /// <summary>
    /// Restarts the TMS API, as a deployment does, so it reads its seed again. Its database and the topic are kept.
    /// Every test on this fixture sees the restart, so only a fixture of a test class's own should do it.
    /// </summary>
    protected async Task RestartTmsAsync(CancellationToken cancellationToken)
    {
        using var restart = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        restart.CancelAfter(StartupTimeout);
        var result = await _app!.ResourceCommands.ExecuteCommandAsync("tms-api", KnownResourceCommands.RestartCommand, restart.Token);
        result.Success.Should().BeTrue(result.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", restart.Token);
    }

    /// <summary>A client for the TMS API that sends the given token, or none.</summary>
    public HttpClient CreateTmsClient(string? token)
    {
        var client = _app!.CreateHttpClient("tms-api");
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// A token for the TMS API as Entra issues it to a managed identity: its client ID in <c>azp</c> and its app roles
    /// in <c>roles</c>. By default it is the intake Logic App's, for the TMS, signed with the key the TMS trusts; change
    /// any of those to show what the TMS refuses.
    /// </summary>
    public string IssueToken(
        string[] roles,
        string audience = LocalTokens.TmsAudience,
        string clientId = LocalTokens.IntakeLogicAppClientId,
        string? signingKey = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = roles },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? _tokenSigningKey)),
                SecurityAlgorithms.HmacSha256),
        });

    // Set NORVIA_TEST_LOG_FILE to also keep the TMS's own logs from a test run, for diagnosing a failure.
    private void CollectTmsLogs()
    {
        var logFile = Environment.GetEnvironmentVariable("NORVIA_TEST_LOG_FILE");
        var resourceLogs = _app!.Services.GetRequiredService<ResourceLoggerService>();
        var tmsApi = _app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "tms-api");
        _ = Task.Run(async () =>
        {
            await foreach (var batch in resourceLogs.WatchAsync(tmsApi))
            {
                foreach (var line in batch)
                {
                    if (TmsLog.TryParse(line.Content) is { } log)
                    {
                        _tmsLogs.Enqueue(log);
                    }
                }

                if (!string.IsNullOrEmpty(logFile))
                {
                    await File.AppendAllLinesAsync(logFile, batch.Select(line => line.Content));
                }
            }
        });
    }

    public virtual async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            await processor.DisposeAsync();
        }

        if (_serviceBus is not null)
        {
            await _serviceBus.DisposeAsync();
        }

        Tms?.Dispose();
        TmsUi?.Dispose();
        CarrierAnswer?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
