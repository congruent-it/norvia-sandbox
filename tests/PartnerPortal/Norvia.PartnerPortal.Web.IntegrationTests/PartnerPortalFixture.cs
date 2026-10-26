extern alias PortalWeb;

using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.AppHost;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using NSubstitute;
using PortalWeb::Norvia.PartnerPortal.Web.Bookings;

[assembly: AssemblyFixture(typeof(Norvia.PartnerPortal.Web.IntegrationTests.PartnerPortalFixture))]

namespace Norvia.PartnerPortal.Web.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to SQL Server and the Partner portal's migrations, and starts the portal in the tests' process against that
/// database. The Norvia Partner API is the portal's one outbound dependency and is replaced at the edge: it answers every booking as Norvia would accept it. Two portals share the
/// database, one that takes the Simulator's reset and one that doesn't.
/// </summary>
public sealed class PartnerPortalFixture : IAsyncLifetime
{
    /// <summary>The credential the portal's reset endpoint asks of the Simulator.</summary>
    public const string ResetKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The credential of every Carrier Persona's booking endpoint.</summary>
    public const string CarrierKey = "carrier-0123456789abcdef";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly List<PortalWebHost> _hosts = [];
    private DistributedApplication? _app;

    /// <summary>A portal whose reset is switched on, as it is locally.</summary>
    public PortalWebHost ResetOn { get; private set; } = null!;

    /// <summary>A portal whose reset is switched off, as it is in Azure unless the estate switches it on.</summary>
    public PortalWebHost ResetOff { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>([], startup.Token);

        // The portal's records are all the tests need: its database and the migrations that make it. The applications, Service Bus and storage only cost startup time and memory.
        foreach (var resource in builder.Resources.Where(IsUnneeded).ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("partner-portal-migrations", KnownResourceStates.Finished, startup.Token);

        var connectionString = await _app.GetConnectionStringAsync("partner-portal", startup.Token)
            ?? throw new InvalidOperationException("The Partner portal's database has no connection string.");
        ResetOn = StartPortal(connectionString, resetEnabled: true);
        ResetOff = StartPortal(connectionString, resetEnabled: false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static bool IsUnneeded(IResource resource) =>
        resource.Name is "dbgate" or "partner-portal-web"
        || resource is ProjectResource && resource.Name != "partner-portal-migrations"
        || resource.GetType().Name.Contains("ServiceBus", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Storage", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Blob", StringComparison.Ordinal);

    private PortalWebHost StartPortal(string connectionString, bool resetEnabled)
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "norvia.seed.json");
        var personas = JsonNode.Parse(File.ReadAllText(seedPath))!["personas"]!.AsArray().OfType<JsonObject>().ToList();
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:partner-portal"] = connectionString,
            ["Seed:Path"] = seedPath,
            ["Reset:Enabled"] = resetEnabled ? "true" : "false",
            ["ResetEndpoint:ApiKey"] = ResetKey,
            ["PartnerApi:BaseAddress"] = "http://127.0.0.1:1/partner/v1",
            ["PartnerApi:PersonaClientIds"] = string.Join(';', personas.Select(persona => $"{persona["id"]}={persona["id"]}-client")),
            ["PartnerApi:LocalTokens:Issuer"] = LocalTokens.Issuer,
            ["PartnerApi:LocalTokens:Audience"] = LocalTokens.PartnerApiAudience,
            ["PartnerApi:LocalTokens:SigningKey"] = "0123456789abcdef0123456789abcdef0123456789abcdef",
            ["DriverApp:Address"] = "http://127.0.0.1:1",
            ["Faults:Owners:driver-app:Address"] = "http://127.0.0.1:1",
            ["Faults:Owners:driver-app:Audience"] = LocalTokens.DriverAppAudience,
            ["Faults:Owners:tms:Address"] = "http://127.0.0.1:1",
            ["Faults:Owners:tms:Audience"] = LocalTokens.TmsAudience,
            ["Faults:LocalTokens:Issuer"] = LocalTokens.Issuer,
            ["Faults:LocalTokens:ClientId"] = LocalTokens.PartnerPortalClientId,
            ["Faults:LocalTokens:SigningKey"] = Convert.ToBase64String("0123456789abcdef0123456789abcdef0123456789abcdef"u8),
            ["Faults:LogicApp:FlagPath"] = Path.Combine(Path.GetTempPath(), $"norvia-driver-reports-disabled-{Guid.NewGuid():N}.flag"),
        };
        foreach (var persona in personas)
        {
            var id = persona["id"]!.GetValue<string>();
            var party = persona["party"]!;
            settings[$"PartnerApi:SubscriptionKeys:{id}"] = $"{id}-subscription";
            settings[$"{(party["type"]!.GetValue<string>() == "Carrier" ? "CarrierEndpoint" : "ShipperEndpoint")}:ApiKeys:{party["id"]!.GetValue<string>()}"] = CarrierKey;
        }

        var host = new PortalWebHost(settings);
        _hosts.Add(host);
        return host;
    }
}

/// <summary>The Partner portal running in the tests' process: its pages' door, <see cref="IPartnerPortal"/>, and an HTTP client for its endpoints.</summary>
public sealed class PortalWebHost : IAsyncDisposable
{
    private readonly PortalFactory _factory;

    internal PortalWebHost(IReadOnlyDictionary<string, string> settings)
    {
        PartnerApi = NorviaAsAlwaysAccepting();
        _factory = new PortalFactory(settings, PartnerApi);
        Portal = _factory.Services.GetRequiredService<IPartnerPortal>();
        Http = _factory.CreateClient();
    }

    /// <summary>What the portal's pages ask of it.</summary>
    public IPartnerPortal Portal { get; }

    /// <summary>
    /// Norvia's Partner API at the edge. Out of the box it takes every booking, answer, Tracking event and Carrier invoice as Norvia would accept it, every invoice
    /// line Matched. A test that needs another answer sets it for its own invoice number; the tests run one at a time.
    /// </summary>
    public INorviaPartnerApi PartnerApi { get; }

    /// <summary>The portal's endpoints, called as the Simulator and the Logic Apps' stand-ins call them.</summary>
    public HttpClient Http { get; }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _factory.DisposeAsync();
    }

    private static INorviaPartnerApi NorviaAsAlwaysAccepting()
    {
        var partnerApi = Substitute.For<INorviaPartnerApi>();
        partnerApi.SubmitAsync(Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(NorviaAnswer.Accepted("TO-0000001", Guid.NewGuid().ToString())));
        partnerApi.AnswerAsync(
                Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(NorviaReply.Recorded(Guid.NewGuid().ToString())));
        partnerApi.SubmitTrackingAsync(
                Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<string>(), Arg.Any<CarrierTrackingEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(TrackingReply.Accepted(Guid.NewGuid().ToString())));
        partnerApi.SubmitCarrierInvoiceAsync(
                Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var lines = call.Arg<IReadOnlyList<CarrierInvoiceLineDraft>>();
                var receipt = new CarrierInvoiceReceipt(
                    Repeated: false, CarrierInvoiceStatus.Approved, lines.Sum(line => line.Amount),
                    [.. lines.Select(line => new CarrierInvoiceReceiptLine(line.BookingRef, line.Amount, CarrierInvoiceLineResult.Matched))]);
                return Task.FromResult(CarrierInvoiceReply.Received(Guid.NewGuid().ToString(), receipt));
            });
        return partnerApi;
    }

    private sealed class PortalFactory(IReadOnlyDictionary<string, string> settings, INorviaPartnerApi partnerApi) : WebApplicationFactory<PortalWeb::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in settings)
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureServices(services =>
            {
                // Norvia answers as the test host's substitute does: see PortalWebHost.PartnerApi.
                services.AddSingleton(partnerApi);
            });
        }
    }
}
