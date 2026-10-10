using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Norvia.DriverApp.Api.IntegrationTests.DriverAppFixture))]

namespace Norvia.DriverApp.Api.IntegrationTests;

/// <summary>Runs the AppHost once for the whole test run, cut down to the Driver app, its migrations and its database.</summary>
public sealed class DriverAppFixture : IAsyncLifetime
{
    // What the Driver app's tests don't call only costs startup time and memory.
    private static readonly string[] Unneeded =
    [
        "dbgate", "tms", "tms-migrations", "tms-api", "tms-web", "partner-portal", "partner-portal-migrations", "partner-portal-web",
        "integration-console-web", "partner-api", "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker",
        "tracking-worker", "proof-of-delivery-worker", "messaging", "storage", "blobs",
    ];
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private DistributedApplication? _app;

    /// <summary>Calls the Driver app's API as the Trip dispatch Logic App and the Driver app's own screens do.</summary>
    public HttpClient DriverApp { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name.StartsWith($"{name}-", StringComparison.Ordinal) && resource.Name.EndsWith("-rebuilder", StringComparison.Ordinal)))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("driver-app-api", startup.Token);
        DriverApp = _app.CreateHttpClient("driver-app-api");
    }

    public async ValueTask DisposeAsync()
    {
        DriverApp?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>A sample Trip version from the Canonical Model's samples, with the fields a test changes set.</summary>
    public static async Task<string> SampleAsync(string name, string tripNumber, int version, string driverId)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", $"{name}.json"), TestContext.Current.CancellationToken);
        var trip = JsonNode.Parse(json)!.AsObject();
        trip["tripNumber"] = tripNumber;
        trip["version"] = version;
        trip["driver"]!["driverId"] = driverId;
        return trip.ToJsonString();
    }
}
