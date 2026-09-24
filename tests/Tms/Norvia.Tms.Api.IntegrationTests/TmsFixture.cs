using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Norvia.Tms.Api.IntegrationTests.TmsFixture))]

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run (TMS, SQL and the Service Bus emulator) and records every
/// message published to the Transport Order events topic through a subscription of its own.
/// </summary>
public sealed class TmsFixture : IAsyncLifetime
{
    public const string TransportOrderEventsTopic = "transport-order-events";
    private const string TestSubscription = "tms-api-integration-tests";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentQueue<ServiceBusReceivedMessage> _published = new();
    private DistributedApplication? _app;
    private ServiceBusClient? _serviceBus;
    private ServiceBusProcessor? _processor;

    public HttpClient Tms { get; private set; } = null!;

    public IEnumerable<ServiceBusReceivedMessage> Published => _published;

    /// <summary>The links the Aspire dashboard shows for the TMS API.</summary>
    public IReadOnlyList<Uri> TmsDashboardUrls =>
        _app!.ResourceNotifications.TryGetCurrentState("tms-api", out var state)
            ? [.. state.Snapshot.Urls.Select(url => new Uri(url.Url))]
            : [];

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(startup.Token);
        var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Single(t => t.TopicName == TransportOrderEventsTopic);
        builder.CreateResourceBuilder(topic).AddServiceBusSubscription(TestSubscription);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        CopyTmsLogsToFile();
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", startup.Token);

        Tms = _app.CreateHttpClient("tms-api");

        _serviceBus = new ServiceBusClient(await _app.GetConnectionStringAsync("messaging", startup.Token));
        _processor = _serviceBus.CreateProcessor(TransportOrderEventsTopic, TestSubscription);
        _processor.ProcessMessageAsync += args =>
        {
            _published.Enqueue(args.Message);
            return Task.CompletedTask;
        };
        _processor.ProcessErrorAsync += _ => Task.CompletedTask;
        await _processor.StartProcessingAsync(startup.Token);
    }

    // Set NORVIA_TEST_LOG_FILE to keep the TMS's own logs from a test run, for diagnosing a failure.
    private void CopyTmsLogsToFile()
    {
        var logFile = Environment.GetEnvironmentVariable("NORVIA_TEST_LOG_FILE");
        if (string.IsNullOrEmpty(logFile))
        {
            return;
        }

        var resourceLogs = _app!.Services.GetRequiredService<ResourceLoggerService>();
        var tmsApi = _app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "tms-api");
        _ = Task.Run(async () =>
        {
            await foreach (var batch in resourceLogs.WatchAsync(tmsApi))
            {
                await File.AppendAllLinesAsync(logFile, batch.Select(line => line.Content));
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }

        if (_serviceBus is not null)
        {
            await _serviceBus.DisposeAsync();
        }

        Tms?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
