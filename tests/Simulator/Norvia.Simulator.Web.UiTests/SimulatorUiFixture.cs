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
/// the Integration console (the log's Message trail links point at it), the TMS UI (the log's Transport Order, Shipment and Trip links point at it, and the Simulator waits for its address), the Partner portal (the Shipper's tracking view, which needs the Driver app API its settings point at), the Trip dispatch and Driver reports workers (a dispatched Trip only reaches the Driver app
/// through the first, and the Drivers' reports only reach the TMS through the second) and the Simulator with its database. A headless Chromium drives the Simulator, which sends its bookings to the real Partner API.
/// Three more Simulators share its database and settings, each changed in one thing the reset journeys need: an operator who may only read, and two whose TMS API
/// is a <see cref="ResetStub"/> that switches its reset off or refuses it.
/// </summary>
public sealed class SimulatorUiFixture : IAsyncLifetime
{
    // What the booking path doesn't use only costs startup time and memory. The workers that empty the Service Bus subscriptions the reset waits on stay.
    private static readonly string[] Unneeded =
    [
        "dbgate", "driver-app-web",
    ];

    // The seed's Carrier Rate Cards cover every Lane of this Shipper and none of the other's, which decides whether the TMS can price a release.
    private const string PricedShipper = "SHP-0001";

    // The TMS gives a Vehicle and a Driver one Trip a day, so the Own fleet is one of each and only the Trips journey puts orders on it.
    private const string OwnFleetVehicle = "VEH-0003";
    private const string OwnFleetDriver = "DRV-0003";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private string? _seedCopy;
    private ResetStub? _stub;

    public Uri Address { get; private set; } = null!;

    /// <summary>The Partner portal, where the Shipper follows its Transport Orders.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

    /// <summary>The TMS UI, where the Transport Orders a run booked are listed.</summary>
    public Uri TmsUi { get; private set; } = null!;

    /// <summary>A Simulator whose operator holds only the Read role.</summary>
    public Uri Viewer { get; private set; } = null!;

    /// <summary>A Simulator whose TMS API says its reset is switched off.</summary>
    public Uri ResetSwitchedOff { get; private set; } = null!;

    /// <summary>A Simulator whose TMS API refuses the reset, with <see cref="ResetStub.RefusalDetail"/>.</summary>
    public Uri ResetRefused { get; private set; } = null!;

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

        // A copy of the seed in which only the priced Shipper, one Vehicle and one Driver are active, so every Shipment the Simulator books can be released to a Carrier.
        var simulator = builder.Resources.Single(resource => resource.Name == "simulator-web");
        _seedCopy = await CopySeedOfAsync(PricedShipper, OwnFleetVehicle, OwnFleetDriver, startup.Token);
        builder.CreateResourceBuilder((ProjectResource)simulator).WithEnvironment("Seed__Path", _seedCopy);

        _stub = ResetStub.Start();
        AddVariant(builder, simulator, "simulator-viewer", ("LocalOperator__Roles__0", "Simulator.Read"), ("LocalOperator__Roles__1", ""));
        AddVariant(builder, simulator, "simulator-reset-off", ("TmsApi__BaseAddress", _stub.SwitchedOff.ToString()));
        AddVariant(builder, simulator, "simulator-reset-refused", ("TmsApi__BaseAddress", _stub.Refusing.ToString()));

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api", "partner-portal-web", "tms-web", "simulator-web", "simulator-viewer", "simulator-reset-off", "simulator-reset-refused" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Address = _app.GetEndpoint("simulator-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        TmsUi = _app.GetEndpoint("tms-web", "http");
        Viewer = _app.GetEndpoint("simulator-viewer", "http");
        ResetSwitchedOff = _app.GetEndpoint("simulator-reset-off", "http");
        ResetRefused = _app.GetEndpoint("simulator-reset-refused", "http");

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

        _stub?.Dispose();
        if (_seedCopy is not null)
        {
            File.Delete(_seedCopy);
        }
    }

    // Another Simulator on the same database, with the same settings and waits as the first, and the given settings changed.
    private static void AddVariant(IDistributedApplicationTestingBuilder builder, IResource simulator, string name, params (string Name, string Value)[] changes)
    {
        var variant = builder.AddProject<Projects.Norvia_Simulator_Web>(name).WithHttpHealthCheck("/health");
        // Annotations can't be shared between resources (the first would lose its settings), so each one is made again. The changes come last, to win over the copied settings.
        foreach (var annotation in simulator.Annotations.ToList())
        {
            switch (annotation)
            {
                case WaitAnnotation wait when wait.WaitType == WaitType.WaitUntilHealthy:
                    variant.WaitFor(builder.CreateResourceBuilder(wait.Resource));
                    break;
                case WaitAnnotation wait:
                    variant.WaitForCompletion(builder.CreateResourceBuilder(wait.Resource));
                    break;
                case EnvironmentCallbackAnnotation callback:
                    variant.WithEnvironment(callback.Callback);
                    break;
                default:
                    // The plain settings are annotations of a type that is not public, which carry the same callback.
                    if (annotation.GetType().Name == "EnvironmentAnnotation" && annotation.GetType().GetProperty("Callback")?.GetValue(annotation) is Func<EnvironmentCallbackContext, Task> setting)
                    {
                        variant.WithEnvironment(setting);
                    }

                    break;
            }
        }

        foreach (var (setting, value) in changes)
        {
            variant.WithEnvironment(setting, value);
        }
    }

    private static async Task<string> CopySeedOfAsync(string shipperId, string vehicleId, string driverId, CancellationToken cancellationToken)
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

        foreach (var vehicle in seed["vehicles"]!.AsArray().OfType<JsonObject>())
        {
            vehicle["active"] = vehicle["id"]!.GetValue<string>() == vehicleId;
        }

        foreach (var driver in seed["drivers"]!.AsArray().OfType<JsonObject>())
        {
            driver["active"] = driver["id"]!.GetValue<string>() == driverId;
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(copy, seed.ToJsonString(), cancellationToken);
        return copy;
    }
}
