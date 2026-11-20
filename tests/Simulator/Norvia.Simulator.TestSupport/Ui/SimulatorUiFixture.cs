using System.Net;
using System.Reflection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Norvia.AppHost;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.TestSupport.Ui;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run, cut down to what a booking needs: SQL Server, the Service Bus emulator, the TMS and
/// its migrations, the local Partner API, the tracking and Proof of delivery workers (the Carriers' tracking only reaches the TMS through them),
/// the Integration console (the log's Message trail links point at it), the TMS UI (the log's Transport Order, Shipment and Trip links point at it, and the Simulator waits for its address), the Partner portal (the Shipper's tracking view, which needs the Driver app API its settings point at), the Trip dispatch and Driver reports workers (a dispatched Trip only reaches the Driver app
/// through the first, and the Drivers' reports only reach the TMS through the second) and the Simulator with its database. A headless Chromium drives the Simulator, which sends its bookings to the real Partner API.
/// The test assembly names more Simulators with <see cref="SimulatorVariantsAttribute"/>, which share the first one's database and settings and each change one thing a journey needs: an operator who may only read, two whose TMS API
/// is a <see cref="TmsResetStub"/> that switches its reset off or refuses it, three with the Agents on (one whose model fails for one profile, one with an Own fleet of its own for the Driver texts), and one whose Own fleet is another Vehicle and Driver, so the signatures journey gets a Trip of its own on a day.
/// Only the Simulators a test assembly names are started. A test assembly registers this class as its assembly fixture.
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

    private static readonly string[] AlwaysStarted = ["tms-api", "partner-api", "partner-portal-web", "tms-web", "simulator-web"];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private string? _seedCopy;
    private string? _signatureSeedCopy;
    private string? _driverTextsSeedCopy;
    private TmsResetStub? _stub;
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

    /// <summary>A Simulator whose TMS API refuses the reset, with <see cref="TmsResetStub.RefusalDetail"/>.</summary>
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
        builder.CreateResourceBuilder((DotnetProjectResource)simulator).WithEnvironment("Seed__Path", _seedCopy);

        var wanted = (Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("The test assembly is the entry assembly."))
            .GetCustomAttributes<SimulatorVariantsAttribute>().SelectMany(attribute => attribute.Names).ToHashSet();
        var drivers = new List<string> { OwnFleetDriver };
        if (wanted.Contains(SimulatorVariants.Signatures))
        {
            _signatureSeedCopy = await CopySeedOfAsync(PricedShipper, SignatureFleetVehicle, SignatureFleetDriver, startup.Token);
            AddVariant(builder, simulator, SimulatorVariants.Signatures, ("Seed__Path", _signatureSeedCopy));
            drivers.Add(SignatureFleetDriver);
        }

        if (wanted.Contains(SimulatorVariants.Agents))
        {
            AddVariant(builder, simulator, SimulatorVariants.Agents, ("Agents__Enabled", "true"));
        }

        if (wanted.Contains(SimulatorVariants.AgentsWithDriverFleet))
        {
            _driverTextsSeedCopy = await CopySeedOfAsync(PricedShipper, DriverTextsFleetVehicle, DriverTextsFleetDriver, startup.Token);
            AddVariant(builder, simulator, SimulatorVariants.AgentsWithDriverFleet, ("Agents__Enabled", "true"), ("Seed__Path", _driverTextsSeedCopy));
            drivers.Add(DriverTextsFleetDriver);
        }

        if (wanted.Contains(SimulatorVariants.FailingAgent))
        {
            // The scripted model reads the language from the question's "language:" line, so a style that carries a line of its own with a language it doesn't know makes it answer with no texts.
            AddVariant(builder, simulator, SimulatorVariants.FailingAgent, ("Agents__Enabled", "true"), ("Personas__Profiles__1__Style", "a few blunt words\nlanguage: tlh"));
        }

        if (wanted.Contains(SimulatorVariants.Viewer))
        {
            AddVariant(builder, simulator, SimulatorVariants.Viewer, ("LocalOperator__Roles__0", "Simulator.Read"), ("LocalOperator__Roles__1", ""));
        }

        if (wanted.Contains(SimulatorVariants.ResetSwitchedOff) || wanted.Contains(SimulatorVariants.ResetRefused))
        {
            _stub = TmsResetStub.Start();
            if (wanted.Contains(SimulatorVariants.ResetSwitchedOff))
            {
                AddVariant(builder, simulator, SimulatorVariants.ResetSwitchedOff, ("TmsApi__BaseAddress", _stub.SwitchedOff.ToString()));
            }

            if (wanted.Contains(SimulatorVariants.ResetRefused))
            {
                AddVariant(builder, simulator, SimulatorVariants.ResetRefused, ("TmsApi__BaseAddress", _stub.Refusing.ToString()));
            }
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in AlwaysStarted.Concat(wanted))
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Address = _app.GetEndpoint("simulator-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        TmsUi = _app.GetEndpoint("tms-web", "http");
        SignatureFleet = EndpointOf(SimulatorVariants.Signatures, wanted);
        WithAgents = EndpointOf(SimulatorVariants.Agents, wanted);
        WithAgentsAndDriverFleet = EndpointOf(SimulatorVariants.AgentsWithDriverFleet, wanted);
        WithFailingAgent = EndpointOf(SimulatorVariants.FailingAgent, wanted);
        Viewer = EndpointOf(SimulatorVariants.Viewer, wanted);
        ResetSwitchedOff = EndpointOf(SimulatorVariants.ResetSwitchedOff, wanted);
        ResetRefused = EndpointOf(SimulatorVariants.ResetRefused, wanted);

        // The Drivers' rosters are Monday to Friday; at the weekend they work today by a change of Roster, as a Planner would make, so the Trip journeys run on any day.
        foreach (var driver in drivers)
        {
            await WorkTodayAsync(driver, startup.Token);
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    // The address of a Simulator this assembly named, and null for one it did not, which none of its journeys use.
    private Uri EndpointOf(string name, HashSet<string> wanted) => wanted.Contains(name) ? _app!.GetEndpoint(name, "http") : null!;

    /// <summary>Stops the TMS API, so nothing can ask it anything, until <see cref="StartTmsAsync"/>.</summary>
    public async Task StopTmsAsync(CancellationToken cancellationToken)
    {
        using var stopping = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync("tms-api", KnownResourceCommands.StopCommand, stopping.Token);
        // What a stopped project reports varies (Exited, Finished, even Unknown); it is down once it is none of these.
        string[] up = [KnownResourceStates.Starting, KnownResourceStates.Running, KnownResourceStates.Stopping, KnownResourceStates.Waiting];
        await _app.ResourceNotifications.WaitForResourceAsync(
            "tms-api", resource => resource.Snapshot.State?.Text is { } state && !up.Contains(state), stopping.Token);
    }

    /// <summary>Starts the TMS API again and waits until it is healthy.</summary>
    public async Task StartTmsAsync(CancellationToken cancellationToken)
    {
        using var starting = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync("tms-api", KnownResourceCommands.StartCommand, starting.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", starting.Token);
    }

    // A resource that never gets there fails the test, rather than hanging the run.
    private static CancellationTokenSource WithinTwoMinutes(CancellationToken cancellationToken)
    {
        var within = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        within.CancelAfter(TimeSpan.FromMinutes(2));
        return within;
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
        var variant = builder.AddDotnetProject(name, ((DotnetProjectResource)simulator).GetProjectMetadata().ProjectPath).WithHttpHealthCheck("/health");
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
