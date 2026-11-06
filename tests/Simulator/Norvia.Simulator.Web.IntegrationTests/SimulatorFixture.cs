extern alias SimulatorWeb;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;
using Norvia.Simulator.Domain.Plans;
using SimulatorWeb::Norvia.Simulator.Web;

[assembly: AssemblyFixture(typeof(Norvia.Simulator.Web.IntegrationTests.SimulatorFixture))]

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to what a booking needs: SQL Server, the Service Bus emulator, the TMS and its
/// migrations, the local Partner API, and the Simulator's database. The Simulator itself runs in the tests' process, started the way the AppHost
/// would start it (its settings are the AppHost's), so the tests reach its doors, <see cref="ISimulatorRequests"/>, which its pages use,
/// without a browser. It sends to the real Partner API, and the fixture reads what the TMS made through the TMS API.
/// </summary>
public sealed class SimulatorFixture : IAsyncLifetime
{
    // What the booking path doesn't use only costs startup time and memory. The Integration console stays: the Simulator's settings point at it.
    // So do the tracking and Proof of delivery workers: the Carriers' tracking and Proofs of delivery only reach the TMS through them. And the Driver app's API and
    // migrations, with the Trip dispatch and Driver reports workers: the Simulator's settings point at the API, the Trip dispatch worker carries each dispatched
    // Trip to it and the Driver reports worker carries the Drivers' reports on to the TMS.
    // The Partner portal and the workers that empty the Service Bus subscriptions the reset waits on stay too: the Simulator's reset resets the Partner portal, and
    // waits for those subscriptions to be empty (ADR 0039).
    private static readonly string[] Unneeded = ["dbgate", "tms-web", "driver-app-web"];

    private static readonly string[] SimulatorSettings =
        ["ConnectionStrings__", "Seed__", "Simulator__", "PartnerApi__", "PartnerPortal__", "TmsApi__", "DriverApp__", "ErpApi__", "IntegrationConsole__", "LocalOperator__"];

    private static readonly string[] IntakeRoles = ["TransportOrders.Intake"];
    private static readonly string[] ManageRoles = ["TransportOrders.Manage"];
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly List<SimulatorHost> _hosts = [];
    private readonly List<string> _seedCopies = [];
    private DistributedApplication? _app;
    private Dictionary<string, string> _settings = [];

    /// <summary>Reads the TMS API as any caller with a TMS token may.</summary>
    public HttpClient Tms { get; private set; } = null!;

    /// <summary>The Simulator, started with the AppHost's settings.</summary>
    public SimulatorHost Simulator { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"], startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        // The AppHost's Simulator stays stopped: the tests run their own, with the same settings, so they reach what its pages ask of it.
        var simulator = builder.Resources.Single(resource => resource.Name == "simulator-web");
        builder.CreateResourceBuilder((ProjectResource)simulator).WithExplicitStart();

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("simulator-migrations", KnownResourceStates.Finished, startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("partner-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("partner-portal-web", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("driver-app-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("erp-api", startup.Token);

        var configuration = await ExecutionConfigurationBuilder.Create(simulator)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { Services = _app.Services }), cancellationToken: startup.Token);
        _settings = configuration.EnvironmentVariables
            .Where(setting => SimulatorSettings.Any(prefix => setting.Key.StartsWith(prefix, StringComparison.Ordinal)))
            .ToDictionary(setting => setting.Key.Replace("__", ":", StringComparison.Ordinal), setting => setting.Value);
        Simulator = StartSimulator();

        Tms = _app.CreateHttpClient("tms-api");
        Tms.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueTmsToken());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        foreach (var copy in _seedCopies)
        {
            File.Delete(copy);
        }

        Tms?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Another Simulator on the same database and the same Partner API, as a page refresh meets: nothing of the first one's memory is in it.
    /// Starting it fails the runs it finds still running, as a restart does.
    /// </summary>
    public SimulatorHost StartSimulator()
    {
        var host = new SimulatorHost(_settings);
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// A Simulator whose seed file lists the given Carriers as the only active ones, so the Planner can be sent to a Carrier the TMS knows as
    /// inactive. Everything else is the seed file's.
    /// </summary>
    public Task<SimulatorHost> StartSimulatorWithActiveCarriersAsync(CancellationToken cancellationToken, params string[] carrierIds) =>
        StartSimulatorWithSeedAsync(cancellationToken, SeedEdits.OnlyCarriers(carrierIds));

    /// <summary>A Simulator whose seed file is changed by the given edits, and is the seed file in everything else.</summary>
    public async Task<SimulatorHost> StartSimulatorWithSeedAsync(CancellationToken cancellationToken, params Action<JsonObject>[] edits)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(_settings["Seed:Path"], cancellationToken))!.AsObject();
        foreach (var edit in edits)
        {
            edit(seed);
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        _seedCopies.Add(copy);
        await File.WriteAllTextAsync(copy, seed.ToJsonString(), cancellationToken);
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["Seed:Path"] = copy });
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// A Simulator with the Agents on, so a plan's pools of Carrier texts are written by the personas agent (the in-process runner with the scripted fake
    /// model), and with the port to the agent changed by <paramref name="configureServices"/> when a test needs to watch it or make it fail.
    /// </summary>
    public async Task<SimulatorHost> StartSimulatorWithAgentsAsync(
        Action<IServiceCollection>? configureServices, CancellationToken cancellationToken, params Action<JsonObject>[] edits)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(_settings["Seed:Path"], cancellationToken))!.AsObject();
        foreach (var edit in edits)
        {
            edit(seed);
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        _seedCopies.Add(copy);
        await File.WriteAllTextAsync(copy, seed.ToJsonString(), cancellationToken);
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["Seed:Path"] = copy, ["Agents:Enabled"] = "true" }, configureServices);
        _hosts.Add(host);
        return host;
    }

    /// <summary>A Simulator whose TMS API address leads nowhere, so none of the Planner's calls reach the TMS.</summary>
    public SimulatorHost StartSimulatorWithoutTms()
    {
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["TmsApi:BaseAddress"] = "http://127.0.0.1:1" });
        _hosts.Add(host);
        return host;
    }

    /// <summary>A Simulator whose seed file is changed by the given edits and whose ERP API address leads nowhere, so none of Finance's calls reach the ERP.</summary>
    public Task<SimulatorHost> StartSimulatorWithoutErpAsync(CancellationToken cancellationToken, params Action<JsonObject>[] edits) =>
        StartSimulatorWithSeedAndSettingAsync("ErpApi:BaseAddress", "http://127.0.0.1:1", edits, cancellationToken);

    /// <summary>A Simulator whose seed file is changed by the given edits and whose Partner portal address leads nowhere, so Finance can invoice but the Shippers' inboxes can't be read.</summary>
    public Task<SimulatorHost> StartSimulatorWithoutPartnerPortalAsync(CancellationToken cancellationToken, params Action<JsonObject>[] edits) =>
        StartSimulatorWithSeedAndSettingAsync("PartnerPortal:BaseAddress", "http://127.0.0.1:1", edits, cancellationToken);

    private async Task<SimulatorHost> StartSimulatorWithSeedAndSettingAsync(string name, string value, Action<JsonObject>[] edits, CancellationToken cancellationToken)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(_settings["Seed:Path"], cancellationToken))!.AsObject();
        foreach (var edit in edits)
        {
            edit(seed);
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        _seedCopies.Add(copy);
        await File.WriteAllTextAsync(copy, seed.ToJsonString(), cancellationToken);
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["Seed:Path"] = copy, [name] = value });
        _hosts.Add(host);
        return host;
    }

    /// <summary>A Simulator whose TMS API is at the address, such as a stand-in that refuses the reset. Everything else is the AppHost's.</summary>
    public SimulatorHost StartSimulatorWithTmsAt(Uri address)
    {
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["TmsApi:BaseAddress"] = address.ToString() });
        _hosts.Add(host);
        return host;
    }

    /// <summary>A Simulator whose ERP API is at the address, such as a stand-in that refuses the reset. Everything else is the AppHost's.</summary>
    public SimulatorHost StartSimulatorWithErpAt(Uri address)
    {
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["ErpApi:BaseAddress"] = address.ToString() });
        _hosts.Add(host);
        return host;
    }

    /// <summary>Stops one of the AppHost's resources, such as a worker that empties a Service Bus subscription, and waits until it has ended.</summary>
    public Task StopAsync(string resourceName, CancellationToken cancellationToken) =>
        ExecuteAsync(resourceName, KnownResourceCommands.StopCommand, state => KnownResourceStates.TerminalStates.Contains(state), cancellationToken);

    /// <summary>Starts a resource that <see cref="StopAsync"/> stopped, and waits until it is healthy again.</summary>
    public async Task StartAsync(string resourceName, CancellationToken cancellationToken)
    {
        await ExecuteAsync(resourceName, KnownResourceCommands.StartCommand, state => state == KnownResourceStates.Running, cancellationToken);
        await _app!.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);
    }

    /// <summary>The connection string of the Service Bus emulator, for sending a message the way an application would.</summary>
    public async Task<string> ServiceBusConnectionStringAsync(CancellationToken cancellationToken) =>
        await _app!.GetConnectionStringAsync("messaging", cancellationToken) ?? throw new InvalidOperationException("The Service Bus emulator has no connection string.");

    /// <summary>Takes in a booking as the intake Logic App does, outside any run: a Transport Order a person entered by hand. Returns its number.</summary>
    public async Task<string> EnterTransportOrderAsync(string shipperReference, CancellationToken cancellationToken)
    {
        var collection = DateTimeOffset.UtcNow.Date.AddDays(2);
        var booking = new JsonObject
        {
            ["shipperId"] = "SHP-0001",
            ["shipperReference"] = shipperReference,
            ["collection"] = new JsonObject
            {
                ["address"] = Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam"),
                ["window"] = Window(collection.AddHours(6), collection.AddHours(10)),
            },
            ["delivery"] = new JsonObject
            {
                ["address"] = Address("Oudegracht 112", "3511 AW", "Utrecht"),
                ["consignee"] = new JsonObject { ["name"] = "Café De Zwaan", ["contactName"] = "Sanne de Vries", ["phone"] = "+31 30 555 0142", ["email"] = null },
                ["window"] = Window(collection.AddHours(12), collection.AddHours(16)),
            },
            ["goodsLines"] = new JsonArray
            {
                new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 850.5, ["dimensionsCm"] = new JsonObject { ["length"] = 120, ["width"] = 80, ["height"] = 150 } },
            },
            ["instructions"] = "Entered by hand",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative)) { Content = JsonContent.Create(booking) };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var response = await Tms.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["transportOrderNumber"]!.GetValue<string>();
    }

    /// <summary>The Trips the Driver app shows the Driver, read through its API as the Driver, with a token signed with the key the Driver app trusts.</summary>
    public async Task<JsonArray> DriverTripsAsync(string driverId, CancellationToken cancellationToken)
    {
        using var driverApp = _app!.CreateHttpClient("driver-app-api");
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.DriverAppAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.SimulatorClientId, ["roles"] = new[] { driverId } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        });
        driverApp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await driverApp.GetAsync(new Uri("/v1/driver/trips", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
    }

    /// <summary>A Transport Order as the TMS API reads it out, or null when the TMS has none by that number.</summary>
    public async Task<JsonObject?> TransportOrderAsync(string number, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/transport-orders/{Uri.EscapeDataString(number)}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>A Shipment as the TMS API reads it out, or null when the TMS has none by that number.</summary>
    public async Task<JsonObject?> ShipmentAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>What the TMS made of a Shipment's journey: Tracking events, Proofs of delivery, Exceptions and Carrier bookings. Null when the TMS has no such Shipment.</summary>
    public async Task<JsonObject?> TimelineAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/tracking-events", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>A Shipment's current Proof of delivery as the TMS API reads it out, or null when it has none.</summary>
    public async Task<JsonObject?> ProofOfDeliveryAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/proof-of-delivery", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>One file of a Shipment's Proof of delivery, read back through the TMS file endpoint: its content type and its bytes.</summary>
    public async Task<(string? ContentType, byte[] Content)> ProofOfDeliveryFileAsync(string shipmentNumber, Guid fileId, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(
            new Uri($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/proof-of-delivery/files/{fileId}", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    /// <summary>What an AppHost resource, such as the Driver app API or the TMS API, has logged so far.</summary>
    public async Task<string> ResourceLogsAsync(string resourceName, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await foreach (var batch in _app!.Services.GetRequiredService<ResourceLoggerService>().GetAllAsync(resourceName).WithCancellation(timeout.Token))
        {
            foreach (var line in batch)
            {
                text.AppendLine(line.Content);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Lets the Driver work today: the Drivers' rosters are Monday to Friday, so at the weekend the Driver has no Shift and the TMS can put no Trip on today. When
    /// today is a weekend day, the Driver gets the Shift of a weekday for today's weekday, as a Planner would change the Roster; disposing restores the Roster.
    /// On a weekday it changes nothing.
    /// </summary>
    public async Task<IAsyncDisposable> WorkTodayAsync(string driverId, CancellationToken cancellationToken)
    {
        var today = PickupSlot.Today(DateTimeOffset.UtcNow).DayOfWeek;
        if (today is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return new Restore(() => Task.CompletedTask);
        }

        using var manager = _app!.CreateHttpClient("tms-api");
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueTmsToken(ManageRoles));
        var listed = JsonNode.Parse(await manager.GetStringAsync(new Uri("/v1/capacity/rosters", UriKind.Relative), cancellationToken))!.AsArray();
        var roster = listed.Single(candidate => candidate!["driverId"]!.GetValue<string>() == driverId)!["weekdays"]!.AsArray();
        var original = new JsonObject();
        foreach (var day in roster)
        {
            // The weekday is a name or a number, as the TMS API writes enums.
            var weekday = day!["weekday"]!;
            var name = weekday.GetValueKind() == System.Text.Json.JsonValueKind.String ? weekday.GetValue<string>() : ((DayOfWeek)weekday.GetValue<int>()).ToString();
            original[name] = new JsonObject { ["start"] = day["start"]!.DeepClone(), ["end"] = day["end"]!.DeepClone() };
        }

        var shift = original[DayOfWeek.Friday.ToString()] ?? new JsonObject { ["start"] = "08:00:00", ["end"] = "16:00:00" };
        var worksToday = (JsonObject)original.DeepClone();
        worksToday[today.ToString()] = shift.DeepClone();
        await ChangeRosterAsync(manager, driverId, worksToday, cancellationToken);
        return new Restore(async () =>
        {
            using var again = _app!.CreateHttpClient("tms-api");
            again.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueTmsToken(ManageRoles));
            await ChangeRosterAsync(again, driverId, original, CancellationToken.None);
        });
    }

    /// <summary>When the Driver's Shift starts on the day of the week, as the seed file's roster has it.</summary>
    public async Task<TimeOnly> ShiftStartAsync(string driverId, DayOfWeek day, CancellationToken cancellationToken)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(_settings["Seed:Path"], cancellationToken))!.AsObject();
        var driver = seed["drivers"]!.AsArray().Single(candidate => candidate!["id"]!.GetValue<string>() == driverId)!;
        return TimeOnly.Parse(driver["roster"]![day.ToString()]!["start"]!.GetValue<string>(), CultureInfo.InvariantCulture);
    }

    /// <summary>A Trip as the TMS API reads it out, with its Stops, or null when the TMS has none by that number.</summary>
    public async Task<JsonObject?> TripAsync(string tripNumber, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/trips/{Uri.EscapeDataString(tripNumber)}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>The Exceptions the TMS has open now.</summary>
    public async Task<JsonArray> OpenExceptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri("/v1/exceptions/open", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
    }

    /// <summary>The Shipper references of the Transport Orders and Rejected bookings the TMS holds with the status.</summary>
    public async Task<IReadOnlyList<string>> ShipperReferencesAsync(string status, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/transport-orders?status={status}", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var listed = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
        return [.. listed.Select(order => order!["shipperReference"]?.GetValue<string>()).OfType<string>()];
    }

    private static async Task ChangeRosterAsync(HttpClient manager, string driverId, JsonObject weekdays, CancellationToken cancellationToken)
    {
        using var response = await manager.PutAsJsonAsync(new Uri($"/v1/capacity/rosters/{Uri.EscapeDataString(driverId)}", UriKind.Relative), new JsonObject { ["weekdays"] = weekdays.DeepClone() }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static JsonObject Address(string street, string postcode, string city) =>
        new() { ["street"] = street, ["postcode"] = postcode, ["city"] = city, ["country"] = "NL" };

    private static JsonObject Window(DateTimeOffset earliest, DateTimeOffset latest) => new() { ["earliest"] = earliest.ToString("O"), ["latest"] = latest.ToString("O") };

    private sealed class Restore(Func<Task> restore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await restore();
    }

    private async Task ExecuteAsync(string resourceName, string command, Func<string?, bool> reached, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var result = await _app!.ResourceCommands.ExecuteCommandAsync(resourceName, command, timeout.Token);
        result.Success.Should().BeTrue(result.Message);
        await _app.ResourceNotifications.WaitForResourceAsync(resourceName, state => reached(state.Snapshot.State?.Text), timeout.Token);
    }

    /// <summary>A token for the TMS API as Entra issues it to a managed identity. The list needs any TMS token.</summary>
    private string IssueTmsToken(string[]? roles = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = roles ?? IntakeRoles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        });
}
