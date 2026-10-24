using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;

[assembly: AssemblyFixture(typeof(Norvia.IntegrationConsole.Agents.IntegrationTests.AgentServiceBusFixture))]

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to its first Service Bus emulator, with the queues and the subscription the tests
/// dead-letter messages on. Each test has an entity of its own, so none depends on another's messages. <see cref="ConsoleSettings"/> is
/// what the AppHost gives the console locally to find the emulator.
/// </summary>
public sealed class AgentServiceBusFixture : IAsyncLifetime
{
    public const string CountsQueue = "agent-counts-queue";
    public const string EmptyQueue = "agent-empty-queue";
    public const string PeekQueue = "agent-peek-queue";
    public const string UnchangedQueue = "agent-unchanged-queue";
    public const string BodiesQueue = "agent-bodies-queue";
    public const string ExplainQueue = "agent-explain-queue";
    public const string ActedOnQueue = "agent-acted-on-queue";
    public const string OwnClickQueue = "agent-own-click-queue";
    public const string IgnoredQueue = "agent-ignored-queue";
    public const string ForbiddenQueue = "agent-forbidden-queue";

    public const string CountsTopic = "agent-counts-topic";
    public const string CountsSubscription = "agent-counts-sub";

    public const string ResubmitTopic = "agent-resubmit-topic";
    public const string SubscriptionA = "agent-sub-a";
    public const string SubscriptionB = "agent-sub-b";

    private const string Messaging = "messaging";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>Talks to the emulator directly, as the apps do, to send, dead-letter, receive and look.</summary>
    public ServiceBusClient Client { get; private set; } = null!;

    /// <summary>The console's <c>LocalServiceBus</c> configuration for the emulator.</summary>
    public IReadOnlyDictionary<string, string> ConsoleSettings { get; private set; } = new Dictionary<string, string>();

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(startup.Token);
        foreach (var resource in builder.Resources.Where(resource => !IsPartOfMessaging(resource)).ToList())
        {
            builder.Resources.Remove(resource);
        }

        var messaging = builder.CreateResourceBuilder(builder.Resources.OfType<AzureServiceBusResource>().Single(resource => resource.Name == Messaging));
        foreach (var queue in new[] { CountsQueue, EmptyQueue, PeekQueue, UnchangedQueue, BodiesQueue, ExplainQueue, ActedOnQueue, OwnClickQueue, IgnoredQueue, ForbiddenQueue })
        {
            messaging.AddServiceBusQueue(queue);
        }

        messaging.AddServiceBusTopic(CountsTopic).AddServiceBusSubscription($"{CountsTopic}-{CountsSubscription}", CountsSubscription);
        var resubmit = messaging.AddServiceBusTopic(ResubmitTopic);
        resubmit.AddServiceBusSubscription($"{ResubmitTopic}-{SubscriptionA}", SubscriptionA);
        resubmit.AddServiceBusSubscription($"{ResubmitTopic}-{SubscriptionB}", SubscriptionB);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(Messaging, startup.Token);

        var connectionString = await _app.GetConnectionStringAsync(Messaging, startup.Token)
            ?? throw new InvalidOperationException("The Service Bus emulator has no connection string.");
        var management = _app.GetEndpoint(Messaging, "emulatorhealth");
        ConsoleSettings = new Dictionary<string, string>
        {
            [$"LocalServiceBus:Namespaces:{Messaging}:ConnectionString"] = connectionString,
            [$"LocalServiceBus:Namespaces:{Messaging}:ManagementConnectionString"] =
                $"Endpoint=sb://{management.Host}:{management.Port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
        };
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
