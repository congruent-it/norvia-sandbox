extern alias SimulatorWeb;

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;
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
    private static readonly string[] Unneeded =
    [
        "dbgate", "tms-web", "partner-portal", "partner-portal-migrations", "partner-portal-web", "driver-app-migrations", "driver-app-api", "driver-app-web",
        "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker",
        "tracking-worker", "proof-of-delivery-worker",
    ];

    private static readonly string[] SimulatorSettings =
        ["ConnectionStrings__", "Seed__", "Simulator__", "PartnerApi__", "TmsApi__", "IntegrationConsole__", "LocalOperator__"];

    private static readonly string[] IntakeRoles = ["TransportOrders.Intake"];
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
    public SimulatorHost StartSimulatorWithActiveCarriers(params string[] carrierIds)
    {
        var seed = JsonNode.Parse(File.ReadAllText(_settings["Seed:Path"]))!.AsObject();
        foreach (var carrier in seed["carriers"]!.AsArray().OfType<JsonObject>())
        {
            carrier["active"] = carrierIds.Contains(carrier["id"]!.GetValue<string>());
        }

        var copy = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(copy, seed.ToJsonString());
        _seedCopies.Add(copy);
        var host = new SimulatorHost(new Dictionary<string, string>(_settings) { ["Seed:Path"] = copy });
        _hosts.Add(host);
        return host;
    }

    /// <summary>A Shipment as the TMS API reads it out, or null when the TMS has none by that number.</summary>
    public async Task<JsonObject?> ShipmentAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }

    /// <summary>The Shipper references of the Transport Orders and Rejected bookings the TMS holds with the status.</summary>
    public async Task<IReadOnlyList<string>> ShipperReferencesAsync(string status, CancellationToken cancellationToken)
    {
        using var response = await Tms.GetAsync(new Uri($"/v1/transport-orders?status={status}", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var listed = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
        return [.. listed.Select(order => order!["shipperReference"]?.GetValue<string>()).OfType<string>()];
    }

    /// <summary>A token for the TMS API as Entra issues it to a managed identity. The list needs any TMS token.</summary>
    private string IssueTmsToken() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = IntakeRoles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        });
}
