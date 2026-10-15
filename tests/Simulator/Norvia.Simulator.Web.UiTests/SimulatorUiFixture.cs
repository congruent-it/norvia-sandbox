using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.Simulator.Web.UiTests.SimulatorUiFixture))]

namespace Norvia.Simulator.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run, cut down to what a booking needs: SQL Server, the Service Bus emulator, the TMS and
/// its migrations, the local Partner API, the tracking and Proof of delivery workers (the Carriers' tracking only reaches the TMS through them),
/// the Integration console (the log's Message trail links point at it), the Partner portal (the Shipper's tracking view, which needs the Driver app API its settings point at) and the Simulator with
/// its database. A headless Chromium drives the Simulator, which sends its bookings to the real Partner API.
/// </summary>
public sealed class SimulatorUiFixture : IAsyncLifetime
{
    // What the booking path doesn't use only costs startup time and memory.
    private static readonly string[] Unneeded =
    [
        "dbgate", "tms-web", "driver-app-web",
        "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker",
    ];

    // The seed's Carrier Rate Cards cover every Lane of this Shipper and none of the other's, which decides whether the TMS can price a release.
    private const string PricedShipper = "SHP-0001";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private string? _seedCopy;

    public Uri Address { get; private set; } = null!;

    /// <summary>The Partner portal, where the Shipper follows its Transport Orders.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);

        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        // A copy of the seed in which only the priced Shipper is active, so every Shipment the Simulator books can be released to a Carrier.
        var simulator = builder.Resources.Single(resource => resource.Name == "simulator-web");
        _seedCopy = await CopySeedWithOnlyAsync(PricedShipper, startup.Token);
        builder.CreateResourceBuilder((ProjectResource)simulator).WithEnvironment("Seed__Path", _seedCopy);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api", "partner-portal-web", "simulator-web" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Address = _app.GetEndpoint("simulator-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_seedCopy is not null)
        {
            File.Delete(_seedCopy);
        }
    }

    private static async Task<string> CopySeedWithOnlyAsync(string shipperId, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "seed", "norvia.seed.json")))
        {
            root = root.Parent;
        }

        var seedPath = Path.Combine(root?.FullName ?? throw new InvalidOperationException("The seed file was not found above the tests."), "seed", "norvia.seed.json");
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken))!.AsObject();
        foreach (var shipper in seed["shippers"]!.AsArray().OfType<JsonObject>())
        {
            shipper["active"] = shipper["id"]!.GetValue<string>() == shipperId;
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(copy, seed.ToJsonString(), cancellationToken);
        return copy;
    }
}
