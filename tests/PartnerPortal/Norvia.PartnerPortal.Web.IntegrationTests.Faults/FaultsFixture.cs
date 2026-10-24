extern alias PortalWeb;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;
using PortalWeb::Norvia.PartnerPortal.Web.Bookings;

[assembly: AssemblyFixture(typeof(Norvia.PartnerPortal.Web.IntegrationTests.Faults.FaultsFixture))]

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>
/// Runs the AppHost once for the whole test run with every worker and the Partner API's stand-in, the TMS and the two Service Bus emulators, as the Carrier and Shipper
/// faults need them: each fault plays through the real path, from the Persona to the TMS. Only the UIs are left out. The Partner portal runs in the tests' process in the place of the
/// AppHost's, started with the AppHost's settings and listening on a port the booking, Shipper notification and Carrier cancellation workers are given as their Personas' address, so
/// the tests reach its doors, <see cref="IPartnerPortal"/>, which its pages use, and switch a fault in its process as the presenter panel does, without a browser.
/// The workers' retries are short, so a fault that ends in Not delivered doesn't make a test wait for the Azure policy's numbers.
/// </summary>
public sealed class FaultsFixture : IAsyncLifetime
{
    /// <summary>The Carrier Persona the tests book with: Van Dijk Transport, Carrier CAR-0001, which the seed's Rate Cards cover on the Shipper's lanes.</summary>
    public const string CarrierPersona = "van-dijk-transport";

    public const string CarrierId = "CAR-0001";

    /// <summary>The Shipper Persona the tests book as: Brouwer Dranken, Shipper SHP-0001.</summary>
    public const string ShipperPersona = "brouwer-dranken";

    public const string ShipperId = "SHP-0001";

    /// <summary>The queue API Management's stand-in puts a Carrier's tracking message on, on the second emulator.</summary>
    public const string TrackingQueue = "carrier-tracking";

    /// <summary>The queue the Proof of delivery message goes on, on the second emulator.</summary>
    public const string ProofOfDeliveryQueue = "carrier-proof-of-delivery";

    /// <summary>The owner of the faults of the Persona the portal plays, as the presenter panel names it.</summary>
    public const string PartnerPortalOwner = "partner-portal";

    // What the faults don't use only costs startup time and memory.
    private static readonly string[] Unneeded = ["dbgate", "tms-web", "driver-app-web", "simulator-web", "simulator-migrations"];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SendRetryInterval = TimeSpan.FromMilliseconds(500);
    private readonly string _tokenSigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private DistributedApplication? _app;
    private PortalFactory? _portal;
    private ServiceBusClient? _edgeBus;

    /// <summary>What the portal's pages ask of it: the Personas' inboxes and shipments, and the presenter panel's switches.</summary>
    public IPartnerPortal Portal { get; private set; } = null!;

    /// <summary>The portal's address, which the workers send the Personas' booking requests, notifications and cancellations to.</summary>
    public Uri PortalAddress { get; private set; } = null!;

    /// <summary>The keys of what the portal logged, as the Message trail has them.</summary>
    public ScopeCapture Telemetry { get; } = new();

    /// <summary>The TMS API, called as the intake Logic App and the TMS UI call it.</summary>
    public TmsApiClient Tms { get; private set; } = null!;

    /// <summary>The second Service Bus emulator, which holds the tracking and Proof of delivery queues.</summary>
    public ServiceBusClient EdgeBus => _edgeBus!;

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

        // The AppHost's portal stays stopped: the tests run their own, with the same settings, so they reach what its pages ask of it and switch its faults in its process.
        var portal = builder.Resources.Single(resource => resource.Name == "partner-portal-web");
        builder.CreateResourceBuilder((ProjectResource)portal).WithExplicitStart();

        var portalAddress = new Uri($"http://localhost:{FreePort()}");
        PortalAddress = portalAddress;
        var seed = Path.Combine(AppContext.BaseDirectory, "norvia.seed.json");
        var personas = SeedPersona.ReadAll(seed);
        PointWorkerAtPortal(builder, "booking-worker", "Booking", "Carriers", personas.Where(persona => !persona.IsShipper), persona => persona.EndpointPath, portalAddress);
        PointWorkerAtPortal(
            builder, "carrier-cancellation-worker", "CarrierCancellation", "Carriers", personas.Where(persona => !persona.IsShipper), persona => persona.CancellationPath!, portalAddress);
        PointWorkerAtPortal(
            builder, "shipper-notification-worker", "ShipperNotification", "Shippers", personas.Where(persona => persona.IsShipper), persona => persona.EndpointPath, portalAddress);

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("partner-portal-migrations", KnownResourceStates.Finished, startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api", "booking-worker", "carrier-cancellation-worker", "shipper-notification-worker", "tracking-worker", "proof-of-delivery-worker" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        var configuration = await ExecutionConfigurationBuilder.Create(portal)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { Services = _app.Services }), cancellationToken: startup.Token);
        var settings = configuration.EnvironmentVariables
            .Where(setting => setting.Key.Contains("__", StringComparison.Ordinal))
            .ToDictionary(setting => setting.Key.Replace("__", ":", StringComparison.Ordinal), setting => setting.Value);
        settings["Seed:Path"] = seed;
        _portal = new PortalFactory(settings, portalAddress.Port, Telemetry);
        _portal.StartServer();
        Portal = _portal.Services.GetRequiredService<IPartnerPortal>();

        Tms = new TmsApiClient(_app.CreateHttpClient("tms-api"), _tokenSigningKey);
        _edgeBus = new ServiceBusClient(await _app.GetConnectionStringAsync("messaging-2", startup.Token));
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_edgeBus is not null)
        {
            await _edgeBus.DisposeAsync();
        }

        Tms?.Dispose();
        if (_portal is not null)
        {
            await _portal.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Switches the Partner portal's fault on for two minutes, as the presenter panel does. Disposing switches it off, so no test leaves a fault on for the next.</summary>
    public async Task<IAsyncDisposable> SwitchedOnAsync(string fault, CancellationToken cancellationToken)
    {
        var view = await Portal.SwitchFaultAsync(PartnerPortalOwner, fault, TimeSpan.FromMinutes(2), cancellationToken);
        view.On.Should().BeTrue();
        return new SwitchedOn(Portal, fault);
    }

    /// <summary>
    /// The messages in a queue's dead-letter queue, looked at and left there. Waits until one the test is looking for turns up, so a message another test dead-lettered is never
    /// taken for it.
    /// </summary>
    public async Task<ServiceBusReceivedMessage> DeadLetteredAsync(string queue, Func<ServiceBusReceivedMessage, bool> isTheOne, CancellationToken cancellationToken)
    {
        await using var receiver = EdgeBus.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        return await Eventually.Async(
            async () =>
            {
                long? from = null;
                while (true)
                {
                    var batch = await receiver.PeekMessagesAsync(50, from, cancellationToken);
                    if (batch.Count == 0)
                    {
                        return null;
                    }

                    if (batch.FirstOrDefault(isTheOne) is { } found)
                    {
                        return found;
                    }

                    from = batch[^1].SequenceNumber + 1;
                }
            },
            cancellationToken);
    }

    private sealed class SwitchedOn(IPartnerPortal portal, string fault) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await portal.SwitchFaultAsync(PartnerPortalOwner, fault, duration: null, CancellationToken.None);
    }

    private static void PointWorkerAtPortal(
        IDistributedApplicationTestingBuilder builder, string worker, string section, string parties, IEnumerable<SeedPersona> personas, Func<SeedPersona, string> path, Uri portalAddress)
    {
        var resource = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == worker));
        var retry = $"{section}__Retry__";
        resource.WithEnvironment($"{retry}Count", "1")
            .WithEnvironment($"{retry}Interval", SendRetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment($"{retry}MaximumInterval", SendRetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var persona in personas)
        {
            resource.WithEnvironment($"{section}__{parties}__{persona.PartyId}__Address", new Uri(portalAddress, path(persona)).ToString());
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The Partner portal in the tests' process, as the AppHost would start it, listening on the port the workers were given.</summary>
    private sealed class PortalFactory : WebApplicationFactory<PortalWeb::Program>
    {
        private readonly IReadOnlyDictionary<string, string> _settings;
        private readonly ScopeCapture _telemetry;

        public PortalFactory(IReadOnlyDictionary<string, string> settings, int port, ScopeCapture telemetry)
        {
            _settings = settings;
            _telemetry = telemetry;
            UseKestrel(port);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in _settings)
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(_telemetry));
        }
    }
}

/// <summary>The TMS API as the tests call it: the intake Logic App's identity for taking a booking in, the TMS UI's for planning.</summary>
public sealed class TmsApiClient : IDisposable
{
    private static readonly string[] IntakeRoles = ["TransportOrders.Intake"];
    private static readonly string[] ManageRoles = ["TransportOrders.Manage"];
    private readonly HttpClient _http;
    private readonly string _signingKey;

    internal TmsApiClient(HttpClient http, string signingKey)
    {
        _http = http;
        _signingKey = signingKey;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Takes in a booking as the intake Logic App does: a Transport Order of the Shipper's with its own reference. Returns its number.</summary>
    public async Task<string> EnterTransportOrderAsync(string shipperId, string shipperReference, CancellationToken cancellationToken)
    {
        var collection = DateTimeOffset.UtcNow.Date.AddDays(2);
        var booking = new JsonObject
        {
            ["shipperId"] = shipperId,
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
            ["instructions"] = "Entered for a fault test",
        };
        using var request = Request(HttpMethod.Post, "/v1/transport-orders", IntakeRoles, JsonContent.Create(booking));
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return (await SendAsync(request, HttpStatusCode.Created, cancellationToken))["transportOrderNumber"]!.GetValue<string>();
    }

    /// <summary>
    /// Assigns the Transport Order to the Carrier. Returns the Shipment's number. The TMS rates a Transport Order as it takes it in, and an assignment that meets the rating
    /// is answered 409 without a code: it is sent again under the same Idempotency-Key, which makes it safe.
    /// </summary>
    public async Task<string> AssignToCarrierAsync(string transportOrderNumber, string carrierId, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString();
        var assigned = await Eventually.Async(
            async () =>
            {
                using var request = Request(
                    HttpMethod.Post, $"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/assign", ManageRoles,
                    JsonContent.Create(new JsonObject { ["assignedTo"] = "Carrier", ["carrierId"] = carrierId }));
                request.Headers.Add("Idempotency-Key", key);
                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.Conflict && !body.Contains("\"code\"", StringComparison.Ordinal))
                {
                    return null;
                }

                response.IsSuccessStatusCode.Should().BeTrue(body);
                return JsonNode.Parse(body)!.AsObject();
            },
            cancellationToken);
        return assigned["shipmentNumber"]!.GetValue<string>();
    }

    /// <summary>
    /// Releases the Shipment to its Carrier, which prices it from the Carrier's Rate Card. The TMS gets the Rate Cards from the ERP as the estate starts, so a release that
    /// can't be priced yet is tried again. Returns the Carrier booking's number.
    /// </summary>
    public async Task<string> ReleaseAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString();
        var released = await Eventually.Async(
            async () =>
            {
                using var request = Request(HttpMethod.Post, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/release", ManageRoles, content: null);
                request.Headers.Add("Idempotency-Key", key);
                using var response = await _http.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject() : null;
            },
            cancellationToken);
        return released["carrierBookingNumber"]!.GetValue<string>();
    }

    /// <summary>Withdraws a Requested Carrier booking before the Carrier answers.</summary>
    public async Task WithdrawAsync(string carrierBookingNumber, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"/v1/carrier-bookings/{Uri.EscapeDataString(carrierBookingNumber)}/withdraw", ManageRoles, content: null);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        await SendAsync(request, HttpStatusCode.OK, cancellationToken);
    }

    /// <summary>Notifies the Shipper from an Open Exception.</summary>
    public async Task NotifyShipperAsync(string exceptionNumber, string message, CancellationToken cancellationToken)
    {
        using var request = Request(
            HttpMethod.Post, $"/v1/exceptions/{Uri.EscapeDataString(exceptionNumber)}/notify-shipper", ManageRoles,
            JsonContent.Create(new JsonObject { ["message"] = message, ["planner"] = "Fault test" }));
        await SendAsync(request, HttpStatusCode.OK, cancellationToken);
    }

    /// <summary>A Shipment as the TMS API reads it out: its status and its Carrier bookings.</summary>
    public async Task<JsonObject> ShipmentAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}", ManageRoles, content: null);
        return await SendAsync(request, HttpStatusCode.OK, cancellationToken);
    }

    /// <summary>What the TMS made of a Shipment's journey: Tracking events, Proofs of delivery, Exceptions with their notifications and cancellations, and Carrier bookings.</summary>
    public async Task<JsonObject> TimelineAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/tracking-events", ManageRoles, content: null);
        return await SendAsync(request, HttpStatusCode.OK, cancellationToken);
    }

    /// <summary>The Shipments the Planner can release, each with how its last Carrier booking ended.</summary>
    public async Task<JsonArray> ShipmentsToReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "/v1/shipments/to-release", ManageRoles, content: null);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!.AsArray();
    }

    /// <summary>A Shipment's current Proof of delivery, or null when it has none.</summary>
    public async Task<JsonObject?> ProofOfDeliveryAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/proof-of-delivery", ManageRoles, content: null);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    private async Task<JsonObject> SendAsync(HttpRequestMessage request, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(expected, body);
        return JsonNode.Parse(body)!.AsObject();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string[] roles, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(roles));
        return request;
    }

    /// <summary>A token for the TMS API as Entra issues it to a managed identity, signed with the key the TMS trusts locally.</summary>
    private string Token(string[] roles) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)), SecurityAlgorithms.HmacSha256),
        });

    private static JsonObject Address(string street, string postcode, string city) =>
        new() { ["street"] = street, ["postcode"] = postcode, ["city"] = city, ["country"] = "NL" };

    private static JsonObject Window(DateTimeOffset earliest, DateTimeOffset latest) => new() { ["earliest"] = earliest.ToString("O"), ["latest"] = latest.ToString("O") };
}
