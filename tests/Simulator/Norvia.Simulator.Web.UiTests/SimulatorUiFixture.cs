using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Norvia.AppHost;
using Norvia.Simulator.Domain.Plans;

[assembly: AssemblyFixture(typeof(Norvia.Simulator.Web.UiTests.SimulatorUiFixture))]

namespace Norvia.Simulator.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run, cut down to what a booking needs: SQL Server, the Service Bus emulator, the TMS and
/// its migrations, the local Partner API, the tracking and Proof of delivery workers (the Carriers' tracking only reaches the TMS through them),
/// the Integration console (the log's Message trail links point at it), the TMS UI (the log's Transport Order, Shipment and Trip links point at it, and the Simulator waits for its address), the Partner portal (the Shipper's tracking view, which needs the Driver app API its settings point at), the Trip dispatch and Driver reports workers (a dispatched Trip only reaches the Driver app
/// through the first, and the Drivers' reports only reach the TMS through the second) and the Simulator with its database. A headless Chromium drives the Simulator, which sends its bookings to the real Partner API.
/// Seven more Simulators share its database and settings, each changed in one thing a journey needs: an operator who may only read, two whose TMS API
/// is a <see cref="ResetStub"/> that switches its reset off or refuses it, three with the Agents on (one whose model fails for one profile, one with an Own fleet of its own for the Driver texts), and one whose Own fleet is another Vehicle and Driver, so the signatures journey gets a Trip of its own on a day.
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
    private static readonly string[] ManageRoles = ["TransportOrders.Manage"];

    private const string SignatureFleetVehicle = "VEH-0001";
    private const string SignatureFleetDriver = "DRV-0001";

    // The Own fleet of the Driver texts journey, with the Agents on. DRV-0002's profile is terse Dutch.
    private const string DriverTextsFleetVehicle = "VEH-0002";
    private const string DriverTextsFleetDriver = "DRV-0002";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private string? _seedCopy;
    private string? _signatureSeedCopy;
    private string? _driverTextsSeedCopy;
    private ResetStub? _stub;
    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, JsonObject> _originalRosters = [];

    public Uri Address { get; private set; } = null!;

    /// <summary>The Partner portal, where the Shipper follows its Transport Orders.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

    /// <summary>The TMS UI, where the Transport Orders a run booked are listed.</summary>
    public Uri TmsUi { get; private set; } = null!;

    /// <summary>A Simulator whose Own fleet is not the first one's, so a Trip of its run is the Trip of another Vehicle and Driver.</summary>
    public Uri SignatureFleet { get; private set; } = null!;

    /// <summary>A Simulator with the Agents on, so a plan's pools of Carrier texts are written by the personas agent (the in-process runner with the scripted fake model).</summary>
    public Uri WithAgents { get; private set; } = null!;

    /// <summary>A Simulator with the Agents on and an Own fleet of its own (DRV-0002, who writes terse Dutch), so a plan's pools of Driver texts are written by the fake model and a Trip of its run is its own on a day.</summary>
    public Uri WithAgentsAndDriverFleet { get; private set; } = null!;

    /// <summary>A Simulator with the Agents on whose model cannot write the terse Dutch pools, so those fall back to the built-in ones.</summary>
    public Uri WithFailingAgent { get; private set; } = null!;

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
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"],
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

        _signatureSeedCopy = await CopySeedOfAsync(PricedShipper, SignatureFleetVehicle, SignatureFleetDriver, startup.Token);
        _driverTextsSeedCopy = await CopySeedOfAsync(PricedShipper, DriverTextsFleetVehicle, DriverTextsFleetDriver, startup.Token);
        _stub = ResetStub.Start();
        AddVariant(builder, simulator, "simulator-signatures", ("Seed__Path", _signatureSeedCopy));
        AddVariant(builder, simulator, "simulator-agents", ("Agents__Enabled", "true"));
        // The scripted model reads the language from the question's "language:" line, so a style that carries a line of its own with a language it doesn't know makes it answer with no texts.
        AddVariant(builder, simulator, "simulator-agents-drivers", ("Agents__Enabled", "true"), ("Seed__Path", _driverTextsSeedCopy));
        AddVariant(builder, simulator, "simulator-agents-failing", ("Agents__Enabled", "true"), ("Personas__Profiles__1__Style", "a few blunt words\nlanguage: tlh"));
        AddVariant(builder, simulator, "simulator-viewer", ("LocalOperator__Roles__0", "Simulator.Read"), ("LocalOperator__Roles__1", ""));
        AddVariant(builder, simulator, "simulator-reset-off", ("TmsApi__BaseAddress", _stub.SwitchedOff.ToString()));
        AddVariant(builder, simulator, "simulator-reset-refused", ("TmsApi__BaseAddress", _stub.Refusing.ToString()));

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api", "partner-portal-web", "tms-web", "simulator-web", "simulator-signatures", "simulator-agents", "simulator-agents-drivers", "simulator-agents-failing", "simulator-viewer", "simulator-reset-off", "simulator-reset-refused" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Address = _app.GetEndpoint("simulator-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        TmsUi = _app.GetEndpoint("tms-web", "http");
        SignatureFleet = _app.GetEndpoint("simulator-signatures", "http");
        WithAgents = _app.GetEndpoint("simulator-agents", "http");
        WithAgentsAndDriverFleet = _app.GetEndpoint("simulator-agents-drivers", "http");
        WithFailingAgent = _app.GetEndpoint("simulator-agents-failing", "http");
        Viewer = _app.GetEndpoint("simulator-viewer", "http");
        ResetSwitchedOff = _app.GetEndpoint("simulator-reset-off", "http");
        ResetRefused = _app.GetEndpoint("simulator-reset-refused", "http");

        // The Drivers' rosters are Monday to Friday; at the weekend they work today by a change of Roster, as a Planner would make, so the Trip journeys run on any day.
        foreach (var driver in new[] { OwnFleetDriver, SignatureFleetDriver, DriverTextsFleetDriver })
        {
            await WorkTodayAsync(driver, startup.Token);
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (driver, roster) in _originalRosters)
        {
            using var manager = ManagerClient();
            await ChangeRosterAsync(manager, driver, roster, CancellationToken.None);
        }

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
        foreach (var copy in new[] { _seedCopy, _signatureSeedCopy, _driverTextsSeedCopy }.OfType<string>())
        {
            File.Delete(copy);
        }
    }

    private HttpClient ManagerClient()
    {
        var client = _app!.CreateHttpClient("tms-api");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = ManageRoles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        }));
        return client;
    }

    // On a weekend day the Driver gets the Shift of a Friday for today's weekday; disposing the fixture restores the Roster. On a weekday nothing changes.
    private async Task WorkTodayAsync(string driverId, CancellationToken cancellationToken)
    {
        var today = PickupSlot.Today(DateTimeOffset.UtcNow).DayOfWeek;
        if (today is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return;
        }

        using var manager = ManagerClient();
        var listed = JsonNode.Parse(await manager.GetStringAsync(new Uri("/v1/capacity/rosters", UriKind.Relative), cancellationToken))!.AsArray();
        var original = new JsonObject();
        foreach (var day in listed.Single(candidate => candidate!["driverId"]!.GetValue<string>() == driverId)!["weekdays"]!.AsArray())
        {
            // The weekday is a name or a number, as the TMS API writes enums.
            var weekday = day!["weekday"]!;
            var name = weekday.GetValueKind() == JsonValueKind.String ? weekday.GetValue<string>() : ((DayOfWeek)weekday.GetValue<int>()).ToString();
            original[name] = new JsonObject { ["start"] = day["start"]!.DeepClone(), ["end"] = day["end"]!.DeepClone() };
        }

        var worksToday = (JsonObject)original.DeepClone();
        worksToday[today.ToString()] = (original[DayOfWeek.Friday.ToString()] ?? new JsonObject { ["start"] = "08:00:00", ["end"] = "16:00:00" }).DeepClone();
        await ChangeRosterAsync(manager, driverId, worksToday, cancellationToken);
        _originalRosters[driverId] = original;
    }

    private static async Task ChangeRosterAsync(HttpClient manager, string driverId, JsonObject weekdays, CancellationToken cancellationToken)
    {
        using var response = await manager.PutAsJsonAsync(new Uri($"/v1/capacity/rosters/{Uri.EscapeDataString(driverId)}", UriKind.Relative), new JsonObject { ["weekdays"] = weekdays.DeepClone() }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
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
