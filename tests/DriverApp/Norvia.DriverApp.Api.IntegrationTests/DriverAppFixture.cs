using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.DriverApp.Api.IntegrationTests.DriverAppFixture))]

namespace Norvia.DriverApp.Api.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to the Driver app (its API and its screens), its migrations and its database, and the
/// Service Bus emulator, where the Driver app publishes what the Driver reports. A subscription of the tests' own records every message the Driver app
/// publishes to the topic for Driver app events. The fixture holds the key the Driver app trusts locally, so it issues tokens the way Entra would.
/// </summary>
public sealed class DriverAppFixture : IAsyncLifetime
{
    public const string DriverAppEventsTopic = "driver-app-events";
    private const string TestSubscription = "driver-app-api-integration-tests";

    /// <summary>The app role only the P12 Driver reports Logic App's managed identity holds on the Driver app.</summary>
    public const string ReportOutcomeRole = "DriverReports.ReportOutcome";

    // What the Driver app's tests don't call only costs startup time and memory. The storage emulator stays: the Driver app's screens store the files of a
    // Proof of delivery in it. The tests play the Driver reports Logic App themselves, so no worker may
    // read the Driver app events or report an outcome.
    private static readonly string[] Unneeded =
    [
        "dbgate", "tms", "tms-migrations", "tms-api", "tms-web", "partner-portal", "partner-portal-migrations", "partner-portal-web",
        "integration-console-web", "partner-api", "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker",
        "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
    ];
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly string _tokenSigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentQueue<ServiceBusReceivedMessage> _published = new();
    private DistributedApplication? _app;
    private ServiceBusClient? _serviceBus;
    private BlobServiceClient? _blobs;
    private ServiceBusProcessor? _processor;

    /// <summary>The credential the Driver app's Trip endpoint asks of the Trip dispatch Logic App; Key Vault holds it in Azure.</summary>
    public const string EndpointKey = "0123456789abcdef0123456789abcdef";

    /// <summary>Calls the Driver app's API as the Trip dispatch Logic App does, with the credential.</summary>
    public HttpClient DriverApp { get; private set; } = null!;

    /// <summary>Calls the Driver app's API without the credential.</summary>
    public HttpClient Anonymous { get; private set; } = null!;

    /// <summary>Calls the Driver app's API as the Driver reports Logic App does, with its token.</summary>
    public HttpClient DriverReports { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:driver-app-endpoint-key={EndpointKey}", $"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"], startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name.StartsWith($"{name}-", StringComparison.Ordinal) && resource.Name.EndsWith("-rebuilder", StringComparison.Ordinal)))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Single(t => t.TopicName == DriverAppEventsTopic);
        builder.CreateResourceBuilder(topic).AddServiceBusSubscription($"{DriverAppEventsTopic}-{TestSubscription}", TestSubscription);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("driver-app-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("driver-app-web", startup.Token);
        DriverApp = _app.CreateHttpClient("driver-app-api");
        DriverApp.DefaultRequestHeaders.Add("X-Api-Key", EndpointKey);
        Anonymous = _app.CreateHttpClient("driver-app-api");
        DriverReports = DriverReportsClient(IssueToken([ReportOutcomeRole]));

        _blobs = new BlobServiceClient(await _app.GetConnectionStringAsync("blobs", startup.Token));
        _serviceBus = new ServiceBusClient(await _app.GetConnectionStringAsync("messaging", startup.Token));
        _processor = _serviceBus.CreateProcessor(DriverAppEventsTopic, TestSubscription);
        _processor.ProcessMessageAsync += args =>
        {
            _published.Enqueue(args.Message);
            return Task.CompletedTask;
        };
        _processor.ProcessErrorAsync += _ => Task.CompletedTask;
        await _processor.StartProcessingAsync(startup.Token);
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

        DriverApp?.Dispose();
        Anonymous?.Dispose();
        DriverReports?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>The names of the blobs in the Driver app's transit container whose names start with the prefix.</summary>
    public async Task<IReadOnlyList<string>> TransitBlobsAsync(string prefix, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await foreach (var blob in _blobs!.GetBlobContainerClient("driver-proofs-of-delivery").GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, cancellationToken))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    /// <summary>The messages the Driver app published for the Trip, oldest first.</summary>
    public IReadOnlyList<ServiceBusReceivedMessage> PublishedFor(string tripNumber) =>
        [.. _published.Where(message => message.SessionId == tripNumber)];

    /// <summary>Waits for the Driver app's outbox to publish the Trip's first message; it polls every second.</summary>
    public async Task<ServiceBusReceivedMessage> FirstPublishedForAsync(string tripNumber, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (PublishedFor(tripNumber) is not [var first, ..])
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        return PublishedFor(tripNumber)[0];
    }

    /// <summary>
    /// A token for the Driver app API as Entra issues it to a managed identity: its client ID in <c>azp</c> and its app roles in <c>roles</c>. Change the
    /// audience or the key to show what the Driver app refuses.
    /// </summary>
    public string IssueToken(string[] roles, string audience = LocalTokens.DriverAppAudience, string? signingKey = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = "norvia-test-p12-driver-reports-logic-app", ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? _tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        });

    /// <summary>
    /// A browser on the Driver app's screens: it keeps cookies, as a browser does, and follows no redirect, so a test sees each answer. A test signs it in
    /// to the local stand-in for Entra.
    /// </summary>
    public HttpClient NewBrowser() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true }) { BaseAddress = _app!.GetEndpoint("driver-app-web") };

    public HttpClient DriverReportsClient(string? token)
    {
        var client = _app!.CreateHttpClient("driver-app-api");
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>A sample Trip version from the Canonical Model's samples, with the fields a test changes set.</summary>
    public static async Task<string> SampleAsync(string name, string tripNumber, int version, string driverId, Action<JsonObject>? edit = null)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", $"{name}.json"), TestContext.Current.CancellationToken);
        var trip = JsonNode.Parse(json)!.AsObject();
        trip["tripNumber"] = tripNumber;
        trip["version"] = version;
        trip["driver"]!["driverId"] = driverId;
        edit?.Invoke(trip);
        return trip.ToJsonString();
    }
}
