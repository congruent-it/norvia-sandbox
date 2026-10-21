using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;

[assembly: AssemblyFixture(typeof(Norvia.IntegrationConsole.Infrastructure.IntegrationTests.ServiceBusFixture))]

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to its first Service Bus emulator, with the queues, topics and
/// subscriptions the tests dead-letter messages on. Each test has entities of its own, so none depends on another's messages. The
/// fixture builds the console's <see cref="IDeadLetterQueues"/> adapter over that emulator the way the console is configured locally.
/// </summary>
public sealed class ServiceBusFixture : IAsyncLifetime
{
    public const string ResubmitQueue = "it-resubmit-queue";
    public const string DiscardQueue = "it-discard-queue";
    public const string PeekQueue = "it-peek-queue";
    public const string CountsQueue = "it-counts-queue";
    public const string EmptyQueue = "it-empty-queue";

    public const string ResubmitTopic = "it-resubmit-topic";
    public const string SubscriptionA = "it-sub-a";
    public const string SubscriptionB = "it-sub-b";

    public const string SessionTopic = "it-session-topic";
    public const string SessionSubscription = "it-session-sub";

    public const string CountsTopic = "it-counts-topic";
    public const string CountsSubscription = "it-counts-sub";

    private const string Messaging = "messaging";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>The adapter under test, reading and changing the emulator's dead-letter queues.</summary>
    public IDeadLetterQueues DeadLetterQueues { get; private set; } = null!;

    /// <summary>Talks to the emulator directly, as the apps do, to send, dead-letter and receive.</summary>
    public ServiceBusClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(startup.Token);
        foreach (var resource in builder.Resources.Where(resource => !IsPartOfMessaging(resource)).ToList())
        {
            builder.Resources.Remove(resource);
        }

        var messaging = builder.CreateResourceBuilder(builder.Resources.OfType<AzureServiceBusResource>().Single(resource => resource.Name == Messaging));
        foreach (var queue in new[] { ResubmitQueue, DiscardQueue, PeekQueue, CountsQueue, EmptyQueue })
        {
            messaging.AddServiceBusQueue(queue);
        }

        var resubmit = messaging.AddServiceBusTopic(ResubmitTopic);
        resubmit.AddServiceBusSubscription($"{ResubmitTopic}-{SubscriptionA}", SubscriptionA);
        resubmit.AddServiceBusSubscription($"{ResubmitTopic}-{SubscriptionB}", SubscriptionB);
        messaging.AddServiceBusTopic(SessionTopic)
            .AddServiceBusSubscription($"{SessionTopic}-{SessionSubscription}", SessionSubscription)
            .WithProperties(subscription => subscription.RequiresSession = true);
        messaging.AddServiceBusTopic(CountsTopic).AddServiceBusSubscription($"{CountsTopic}-{CountsSubscription}", CountsSubscription);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(Messaging, startup.Token);

        var connectionString = await _app.GetConnectionStringAsync(Messaging, startup.Token)
            ?? throw new InvalidOperationException("The Service Bus emulator has no connection string.");
        var management = _app.GetEndpoint(Messaging, "emulatorhealth");
        var options = Options.Create(new LocalServiceBusOptions
        {
            Namespaces =
            {
                [Messaging] = new LocalServiceBusNamespace
                {
                    ConnectionString = connectionString,
                    ManagementConnectionString = $"Endpoint=sb://{management.Host}:{management.Port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                },
            },
        });
        DeadLetterQueues = new ServiceBusDeadLetterQueues(
            new EmulatorServiceBusEntities(options), new ServiceBusClients(new DefaultAzureCredential(), options));
        Client = new ServiceBusClient(connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    // The emulator's own SQL container stays, as does the Azure environment: the pipeline that provisions the emulator's resources depends on its steps.
#pragma warning disable ASPIREAZURE001 // AzureEnvironmentResource is experimental
    private static bool IsPartOfMessaging(IResource resource) =>
        resource is AzureEnvironmentResource or AzureServiceBusResource { Name: Messaging } or ContainerResource { Name: $"{Messaging}-mssql" }
        || resource is IResourceWithParent parent && IsPartOfMessaging(parent.Parent);
#pragma warning restore ASPIREAZURE001
}
