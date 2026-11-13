using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>
/// Runs the AppHost under Aspire's test host, cut down to the stand-in that plays API Management (ADR 0023) and the Aspire dashboard
/// the console reads locally (ADR 0027): the dashboard is on, with the telemetry API key the AppHost hands the console. The stand-in
/// has no TMS, queue or blob store behind it, so what it can answer is what it refuses at the edge, and what it tells the dashboard
/// of its refusals is what the console lists. Needs no deployed estate.
/// </summary>
public sealed class LocalTelemetryFixture : IAsyncLifetime
{
    public const string PartnerApi = "partner-api";
    public const string Dashboard = "aspire-dashboard";
    public const string ApiKey = "test-dashboard-api-key";
    public const string SubscriptionKey = "van-dijk-subscription-key";
    public const string ShipperClientId = "client-van-dijk";
    public const string UnregisteredClientId = "client-unregistered";

    private const string Issuer = "https://norvia.local/tokens";
    private const string Audience = "api://norvia-partner-api";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(4);

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(64);
    private DistributedApplication? _app;

    /// <summary>The address of the dashboard, whose telemetry API the console reads.</summary>
    public Uri DashboardUrl { get; private set; } = null!;

    /// <summary>When the stand-in and the dashboard were ready: what it refused before, a probe for one, isn't a test's.</summary>
    public DateTimeOffset ReadyAt { get; private set; }

    /// <summary>A client of the stand-in.</summary>
    public HttpClient StandIn { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var standInPort = FreePort();
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [
                $"--ASPNETCORE_URLS=http://localhost:{FreePort()}",
                "--ASPIRE_ALLOW_UNSECURED_TRANSPORT=true",
                $"--AppHost:DashboardApiKey={ApiKey}",
            ],
            (options, _) => options.DisableDashboard = false,
            startup.Token);

        foreach (var resource in builder.Resources.Where(resource => resource.Name != PartnerApi && resource is not IResourceWithParent).ToList())
        {
            builder.Resources.Remove(resource);
        }

        // What the AppHost gives the stand-in in the estate of a developer, without the TMS, the queues and the blob store it reaches for the calls it answers.
        var partnerApi = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == PartnerApi));
        foreach (var annotation in partnerApi.Resource.Annotations.Where(annotation => annotation is EnvironmentCallbackAnnotation or WaitAnnotation or ResourceRelationshipAnnotation).ToList())
        {
            partnerApi.Resource.Annotations.Remove(annotation);
        }

        var key = Convert.ToBase64String(_signingKey);
        var unreachable = "http://localhost:1";
        foreach (var (name, value) in new Dictionary<string, string>
        {
            // What Aspire itself gives a project: where to listen, and where the dashboard receives its telemetry.
            ["ASPNETCORE_URLS"] = $"http://localhost:{standInPort}",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["OTEL_SERVICE_NAME"] = PartnerApi,
            ["PartnerApi__Tokens__Issuer"] = Issuer,
            ["PartnerApi__Tokens__Audience"] = Audience,
            ["PartnerApi__Tokens__SigningKey"] = key,
            [$"PartnerApi__SubscriptionKeys__van-dijk"] = SubscriptionKey,
            [$"PartnerApi__ShippersByClientId__{ShipperClientId}"] = "shipper-van-dijk",
            ["Tms__BaseAddress"] = unreachable,
            ["Tms__Tokens__Issuer"] = Issuer,
            ["Tms__Tokens__Audience"] = "api://norvia-tms",
            ["Tms__Tokens__ClientId"] = "intake",
            ["Tms__Tokens__SigningKey"] = key,
            ["Tms__AnswerClientId"] = "answer",
            ["Tms__ShipperTrackingClientId"] = "api-management",
            ["CarrierInvoice__BaseAddress"] = unreachable,
            ["CarrierInvoice__Tokens__Issuer"] = Issuer,
            ["CarrierInvoice__Tokens__Audience"] = "api://norvia-carrier-invoice-logic-app",
            ["CarrierInvoice__Tokens__ClientId"] = "api-management",
            ["CarrierInvoice__Tokens__SigningKey"] = key,
            ["ConnectionStrings__messaging"] = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
            ["ConnectionStrings__blobs"] = "UseDevelopmentStorage=true",
        })
        {
            partnerApi.WithEnvironment(name, value);
        }

        // Where the dashboard receives telemetry is where the test host puts it: known once it runs, which the stand-in waits for.
        partnerApi.WithEnvironment(async context =>
        {
            var notifications = context.ExecutionContext.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.WaitForResourceAsync(Dashboard, KnownResourceStates.Running, context.CancellationToken);
            notifications.TryGetCurrentState(Dashboard, out var dashboard);
            context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = dashboard!.Snapshot.Urls[0].Url;
            // The dashboard asks for the key it was started with, which the test host gives a project as a header.
            if (dashboard.Snapshot.EnvironmentVariables.FirstOrDefault(variable => variable.Name.Equals("DASHBOARD__OTLP__PRIMARYAPIKEY", StringComparison.OrdinalIgnoreCase)) is { } otlpKey)
            {
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_HEADERS"] = $"x-otlp-api-key={otlpKey.Value}";
            }
        });

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await _app.ResourceNotifications.WaitForResourceAsync(PartnerApi, KnownResourceStates.Running, startup.Token);
        _app.ResourceNotifications.TryGetCurrentState(Dashboard, out var dashboardState);
        // The dashboard lists its OTLP/gRPC address first and its web address, which also serves the telemetry API, second.
        DashboardUrl = new Uri(dashboardState!.Snapshot.Urls[1].Url);
        _app.ResourceNotifications.TryGetCurrentState(PartnerApi, out var current);
        var resourceId = current!.ResourceId;
        _ = Task.Run(async () =>
        {
            await foreach (var batch in _app.Services.GetRequiredService<ResourceLoggerService>().WatchAsync(resourceId))
            {
                foreach (var line in batch)
                {
                    logs.Enqueue(line.Content);
                }
            }
        });
        StandIn = new HttpClient { BaseAddress = new Uri($"http://localhost:{standInPort}") };
        try
        {
            await WaitForAsync(StandIn, new Uri("/partner/v1/transport-orders", UriKind.Relative), startup.Token);
            using var dashboard = new HttpClient { BaseAddress = DashboardUrl };
            dashboard.DefaultRequestHeaders.Add(DashboardTrailSource.ApiKeyHeader, ApiKey);
            await WaitForAsync(dashboard, new Uri("/api/telemetry/logs?limit=1", UriKind.Relative), startup.Token);
            ReadyAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"The stand-in never answered. Its log:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        StandIn?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>A token for the Partner API as Entra issues it to a client, with these app roles.</summary>
    public string Token(string clientId, params string[] roles)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256),
        });
    }

    private static async Task WaitForAsync(HttpClient client, Uri path, CancellationToken cancellationToken)
    {
        while (true)
        {
            // The proxy takes a connection before the application is listening, and holds it, so each attempt is short.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var response = await client.GetAsync(path, attempt.Token);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
