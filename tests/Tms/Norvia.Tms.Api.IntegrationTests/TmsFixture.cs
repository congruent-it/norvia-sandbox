using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    public const string TripEventsTopic = "trip-events";
    private const string TestSubscription = "tms-api-integration-tests";
    public const string IntakeRole = "TransportOrders.Intake";

    /// <summary>The app role only the TMS UI's managed identity holds: Customer service's re-rate goes through it.</summary>
    public const string ManageRole = "TransportOrders.Manage";

    /// <summary>The app role only the Carrier-answer Logic App's managed identity holds (P4).</summary>
    public const string AnswerRole = "CarrierBookings.Answer";

    /// <summary>The app role only the P4 booking Logic App's managed identity holds, for its sent and not-delivered reports.</summary>
    public const string ReportRole = "CarrierBookings.Report";

    /// <summary>The app role only the P7 Shipper notification Logic App's managed identity holds.</summary>
    public const string ShipperNotificationReportRole = "ShipperNotifications.Report";

    public const string TripDeliveryReportRole = "Trips.ReportDelivery";

    /// <summary>The app role only the P12 Driver reports Logic App's managed identity holds, for starting a Trip.</summary>
    public const string TripStartRole = "Trips.Start";

    /// <summary>The app role of the Carrier-tracking Logic App's managed identity, which records Tracking events (P5).</summary>
    public const string TrackingRecordRole = "TrackingEvents.Record";

    /// <summary>The app role of the Carrier Proof of delivery Logic App's managed identity, which records Proofs of delivery (P6).</summary>
    public const string ProofOfDeliveryRecordRole = "ProofsOfDelivery.Record";

    /// <summary>The app role only API Management's managed identity holds: a Shipper's tracking queries go through it (P8).</summary>
    public const string ShipperTrackingReadRole = "ShipperTracking.Read";

    /// <summary>The client ID the TMS takes as the WMS handoff's caller. No WMS exists yet, so only the tests call as it.</summary>
    public const string WmsClientId = "norvia-test-wms";

    /// <summary>How often the TMS's outbox looks for a message to publish; quick, so tests don't wait for it.</summary>
    private static readonly TimeSpan OutboxPollingInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long the outbox runs after an event arrives, a few cycles, so a duplicate or an event that shouldn't be sent would show up.</summary>
    private static readonly TimeSpan OutboxSettleTime = OutboxPollingInterval * 8;

    /// <summary>How often a test looks again for something it waits for.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartAttemptTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private const int StartAttempts = 3;
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

    /// <summary>A client holding the Shipper notification report role, as the P7 Shipper notification Logic App's managed identity will.</summary>
    public HttpClient ShipperNotificationReport { get; private set; } = null!;

    /// <summary>The Trip dispatch Logic App, which reports a Trip version as delivered or not delivered.</summary>
    public HttpClient TripDeliveryReport { get; private set; } = null!;

    /// <summary>The Driver reports Logic App, which starts a Trip for its Driver (P12).</summary>
    public HttpClient TripStart { get; private set; } = null!;

    /// <summary>The same Driver reports Logic App as it records a Driver's Stop report as a Tracking event, and a Proof of delivery, from Own fleet (P12).</summary>
    public HttpClient DriverReports { get; private set; } = null!;

    /// <summary>A client holding the record role, as the Carrier-tracking Logic App's managed identity will (P5, later ticket).</summary>
    public HttpClient TrackingRecord { get; private set; } = null!;

    /// <summary>Calls as API Management does for a Shipper's tracking queries.</summary>
    public HttpClient ShipperTracking { get; private set; } = null!;

    /// <summary>A client holding the Proof of delivery record role, as the Carrier Proof of delivery Logic App's managed identity will (P6, later ticket).</summary>
    public HttpClient ProofOfDeliveryRecord { get; private set; } = null!;

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
        using var startup = new CancellationTokenSource(StartAttempts * StartAttemptTimeout);

        // The AppHost now and then doesn't come up on a CI runner: the Service Bus emulator crashes as it starts (exit
        // code 139), or something else keeps the TMS API waiting for good. A crashed container ends the attempt at once,
        // a slow one after StartAttemptTimeout, and the AppHost starts again from scratch. What each attempt found is kept.
        for (var attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(startup.Token);
            attemptTimeout.CancelAfter(StartAttemptTimeout);
            try
            {
                await StartAppAsync(attemptTimeout.Token);
                break;
            }
            catch (Exception ex) when (ex is ContainerExitedException or StartStalledException || (ex is OperationCanceledException && !startup.IsCancellationRequested))
            {
                var description = await DescribeResourcesAsync();
                var logFile = Environment.GetEnvironmentVariable("NORVIA_TEST_LOG_FILE");
                if (!string.IsNullOrEmpty(logFile))
                {
                    await File.AppendAllTextAsync(logFile, $"AppHost start attempt {attempt} failed: {ex.Message}{Environment.NewLine}{description}{Environment.NewLine}", CancellationToken.None);
                }

                await _app!.DisposeAsync();
                _app = null;
                if (attempt == StartAttempts)
                {
                    throw new InvalidOperationException($"The AppHost did not start in {attempt} attempts. The last found:{Environment.NewLine}{description}", ex);
                }
            }
        }

        Tms = CreateTmsClient(IssueToken([IntakeRole]));
        TmsUi = CreateTmsClient(IssueToken([ManageRole], clientId: LocalTokens.TmsUiClientId));
        CarrierAnswer = CreateTmsClient(IssueToken([AnswerRole], clientId: "norvia-test-carrier-answer-logic-app"));
        CarrierReport = CreateTmsClient(IssueToken([ReportRole], clientId: "norvia-test-p4-booking-logic-app"));
        ShipperNotificationReport = CreateTmsClient(IssueToken([ShipperNotificationReportRole], clientId: "norvia-test-p7-shipper-notification-logic-app"));
        TripDeliveryReport = CreateTmsClient(IssueToken([TripDeliveryReportRole], clientId: "norvia-test-p11-trip-dispatch-logic-app"));
        TripStart = CreateTmsClient(IssueToken([TripStartRole], clientId: "norvia-test-p12-driver-reports-logic-app"));
        DriverReports = CreateTmsClient(IssueToken([TripStartRole, TrackingRecordRole, ProofOfDeliveryRecordRole], clientId: "norvia-test-p12-driver-reports-logic-app"));
        TrackingRecord = CreateTmsClient(IssueToken([TrackingRecordRole], clientId: "norvia-test-carrier-tracking-logic-app"));

        ShipperTracking = CreateTmsClient(IssueToken([ShipperTrackingReadRole], clientId: "norvia-test-apim"));
        ProofOfDeliveryRecord = CreateTmsClient(IssueToken([ProofOfDeliveryRecordRole], clientId: "norvia-test-carrier-proof-of-delivery-logic-app"));

        _serviceBus = new ServiceBusClient(await _app!.GetConnectionStringAsync("messaging", startup.Token));
        foreach (var topicName in new[] { TransportOrderEventsTopic, ShipmentEventsTopic, TripEventsTopic })
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

    private async Task StartAppAsync(CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"],
            cancellationToken);
        builder.RemoveWhatTheTmsApiTestsDontUse();
        foreach (var topicName in new[] { TransportOrderEventsTopic, ShipmentEventsTopic, TripEventsTopic })
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

        _app = await builder.BuildAsync(cancellationToken);

        // Starting completes only once every resource has, so a container that exits is watched for meanwhile.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var starting = _app.StartAsync(attempt.Token);
        var healthy = _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", attempt.Token);
        var exited = Task.WhenAny(WaitForAContainerToExitAsync(attempt.Token), WaitForTheStartToStallAsync(attempt.Token)).Unwrap();
        var started = Task.WhenAll(starting, healthy);
        try
        {
            if (await Task.WhenAny(started, exited) == exited)
            {
                await exited;
            }

            await started;
            CollectTmsLogs();
        }
        catch
        {
            // Ends the waits that would never finish, as what they wait for has gone.
            await attempt.CancelAsync();
            await Task.WhenAll(started, exited).ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            throw;
        }

        // The attempt succeeded, so the watch for an exit ends without failing anything.
        await attempt.CancelAsync();
        await exited.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>What every resource of the AppHost is doing, with the end of the log of those that are not running.</summary>
    private async Task<string> DescribeResourcesAsync()
    {
        var model = _app!.Services.GetRequiredService<DistributedApplicationModel>();
        var logger = _app.Services.GetRequiredService<ResourceLoggerService>();
        var description = new StringBuilder();
        foreach (var resource in model.Resources)
        {
            if (!_app.ResourceNotifications.TryGetCurrentState(resource.Name, out var current))
            {
                description.AppendLine(CultureInfo.InvariantCulture, $"{resource.Name}: no state");
                continue;
            }

            var state = current.Snapshot.State?.Text;
            description.AppendLine(CultureInfo.InvariantCulture, $"{resource.Name}: {state}, health {current.Snapshot.HealthStatus}, exit code {current.Snapshot.ExitCode}");
            if (state is "Running" or "Finished" or null)
            {
                continue;
            }

            using var replay = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var lines = new List<string>();
            try
            {
                await foreach (var batch in logger.WatchAsync(resource).WithCancellation(replay.Token))
                {
                    lines.AddRange(batch.Select(line => line.Content));
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // No log was written.
            }

            foreach (var line in lines.TakeLast(25))
            {
                description.AppendLine(CultureInfo.InvariantCulture, $"    {line}");
            }
        }

        description.AppendLine("Machine:");
        description.AppendLine(await RunAsync("sh", "-c", "uptime; free -m; ps -eo pid,pcpu,pmem,etime,args --sort=-pcpu | cut -c1-200 | head -15"));
        return description.ToString();
    }

    private static async Task<string> RunAsync(string fileName, params string[] arguments)
    {
        if (!OperatingSystem.IsLinux())
        {
            return string.Empty;
        }

        try
        {
            var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return output;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return $"({ex.Message})";
        }
    }

    // Now and then a resource keeps waiting for one that is running and healthy (the migrations for SQL Server, on a CI
    // runner's first start), which no timeout of Aspire's ends. When no resource has changed for a minute and none is
    // still starting, nothing will, so the attempt ends.
    private async Task WaitForTheStartToStallAsync(CancellationToken cancellationToken)
    {
        var model = _app!.Services.GetRequiredService<DistributedApplicationModel>();
        var lastSignature = string.Empty;
        var lastChange = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var states = model.Resources
                .Select(resource => _app.ResourceNotifications.TryGetCurrentState(resource.Name, out var current) ? current.Snapshot : null)
                .ToList();
            var signature = string.Join(';', states.Select(snapshot => $"{snapshot?.State?.Text}/{snapshot?.HealthStatus}"));
            if (signature != lastSignature || states.Any(snapshot => snapshot?.State?.Text == "Starting"))
            {
                lastSignature = signature;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange > StallTimeout)
            {
                throw new StartStalledException(StallTimeout);
            }
        }
    }

    private async Task WaitForAContainerToExitAsync(CancellationToken cancellationToken)
    {
        var containerResources = _app!.Services.GetRequiredService<DistributedApplicationModel>().Resources.Where(resource => resource.Annotations.OfType<ContainerImageAnnotation>().Any());
        var exits = containerResources.Select(async container =>
        {
            var exited = await _app.ResourceNotifications.WaitForResourceAsync(
                container.Name, state => KnownResourceStates.TerminalStates.Contains(state.Snapshot.State?.Text), cancellationToken);
            throw new ContainerExitedException(container.Name, exited.Snapshot.ExitCode);
        });
        await await Task.WhenAny(exits);
    }

    /// <summary>What the fixture sets on the TMS API on top of what the AppHost gives it, as environment variables.</summary>
    protected virtual Dictionary<string, string> TmsApiSettings() => new()
    {
        ["Seed__Path"] = Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json"),
        ["IntakeCallers__ClientIdsByChannel__Wms__0"] = WmsClientId,
        ["Rating__SweepInterval"] = "01:00:00",
        ["Outbox__PollingInterval"] = OutboxPollingInterval.ToString("c", CultureInfo.InvariantCulture),

        // A test run has no dashboard to take the TMS's telemetry, and a TMS that stops keeps trying to flush it there
        // for seconds, too long for a restart. Tests read the TMS's console logs instead.
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
    };

    /// <summary>Lets the outbox run a few cycles, so an event that shouldn't be sent, or a duplicate, would show up.</summary>
    public static Task SettleOutboxAsync(CancellationToken cancellationToken) => Task.Delay(OutboxSettleTime, cancellationToken);

    /// <summary>Waits for the TMS to write a log that matches, as logs arrive a moment after the call.</summary>
    public async Task<TmsLog> TmsLogAsync(Func<TmsLog, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!_tmsLogs.Any(match))
        {
            await Task.Delay(PollInterval, timeout.Token);
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
        CarrierReport?.Dispose();
        ShipperNotificationReport?.Dispose();
        TripDeliveryReport?.Dispose();
        TripStart?.Dispose();
        DriverReports?.Dispose();
        TrackingRecord?.Dispose();
        ShipperTracking?.Dispose();
        ProofOfDeliveryRecord?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

internal sealed class ContainerExitedException(string container, int? exitCode)
    : Exception($"Container '{container}' exited during startup with exit code {exitCode}.");

internal sealed class StartStalledException(TimeSpan quietFor)
    : Exception($"No resource of the AppHost changed for {quietFor.TotalSeconds:0} s while it started.");
