extern alias ErpApi;
extern alias ErpWeb;
extern alias InvoiceWorker;
extern alias PartnerApi;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;
using NSubstitute;
using ErpApiProgram = ErpApi::Program;
using ErpWebProgram = ErpWeb::Program;
using InvoiceWorkerProgram = InvoiceWorker::Program;
using PartnerApiProgram = PartnerApi::Program;
using ErpCarrierInvoices = InvoiceWorker::Norvia.Integration.LocalCarrierInvoiceWorker.ErpCarrierInvoices;
using CarrierInvoiceLogicApp = PartnerApi::Norvia.Integration.LocalPartnerApi.CarrierInvoiceLogicApp;

[assembly: AssemblyFixture(typeof(Norvia.Integration.CarrierInvoice.IntegrationTests.CarrierInvoiceFixture))]

namespace Norvia.Integration.CarrierInvoice.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to SQL Server and the ERP's migrations, and starts the local Carrier invoice path in the tests' process against
/// that database: the Partner API's stand-in, the Carrier invoice worker behind it and the ERP billing API behind that, each calling the next over its in-memory server
/// with the identity and token it has locally. One key signs every token, as the AppHost's local key does. The ERP's UI process runs too, to load the master data (the Carriers)
/// and to publish the outbox to a stand-in for Service Bus, whose messages the tests read. Service Bus and Blob storage are the Partner API's other dependencies,
/// which this path never uses.
/// </summary>
public sealed class CarrierInvoiceFixture : IAsyncLifetime
{
    /// <summary>The Carrier Persona the tests send as: its client is Van Dijk Transport, Carrier CAR-0001.</summary>
    public const string CarrierClientId = "norvia-local-persona-van-dijk-transport";

    public const string CarrierId = "CAR-0001";

    /// <summary>A Carrier Persona whose Carrier the ERP doesn't know.</summary>
    public const string StrangerClientId = "norvia-local-persona-stranger-transport";

    public const string ShipperClientId = "norvia-local-persona-brouwer-dranken";

    /// <summary>The Carrier Persona's subscription key for the Norvia partners product.</summary>
    public const string SubscriptionKey = "van-dijk-subscription-key";

    private const string CarrierCostRecordRole = "CarrierCosts.Record";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly byte[] SigningKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
    private static readonly string SigningKeyBase64 = Convert.ToBase64String(SigningKey);

    private readonly ConcurrentQueue<string> _published = new();
    private DistributedApplication? _app;
    private ErpApiFactory? _erpApi;
    private ErpWebFactory? _erpWeb;
    private InvoiceWorkerFactory? _worker;
    private PartnerApiFactory? _partnerApi;

    /// <summary>The Canonical Model event types the ERP's outbox has published to Service Bus.</summary>
    public IReadOnlyList<string> Published => [.. _published];

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>([], startup.Token);

        // The ERP's records are all the tests need: its database and the migrations that make it.
        foreach (var resource in builder.Resources.Where(IsUnneeded).ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("erp-migrations", KnownResourceStates.Finished, startup.Token);
        var connectionString = await _app.GetConnectionStringAsync("erp", startup.Token)
            ?? throw new InvalidOperationException("The ERP's database has no connection string.");

        // The UI's process goes first: it loads the master data the API checks Carriers against.
        _erpWeb = new ErpWebFactory(connectionString, _published);
        _ = _erpWeb.Services;
        _erpApi = new ErpApiFactory(connectionString);
        _ = _erpApi.Server;
        _worker = new InvoiceWorkerFactory(_erpApi.Server.CreateHandler);
        _ = _worker.Server;
        _partnerApi = new PartnerApiFactory(_worker.Server.CreateHandler);
        _ = _partnerApi.Server;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in new IAsyncDisposable?[] { _partnerApi, _worker, _erpApi, _erpWeb, _app })
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }
        }
    }

    /// <summary>A client of the Norvia Partner API, with the subscription key and the token of a Persona's client, as the Partner portal sends them.</summary>
    public HttpClient PartnerApiAs(string clientId, string[]? roles = null, string? subscriptionKey = SubscriptionKey)
    {
        var client = _partnerApi!.CreateClient();
        if (subscriptionKey is not null)
        {
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PersonaToken(clientId, roles ?? ["CarrierInvoices.Submit"]));
        return client;
    }

    /// <summary>Records the Carrier cost the Carrier invoice Logic App's lines are matched against, as the Carrier cost Logic App does.</summary>
    public async Task RecordCarrierCostAsync(string bookingNumber, string bookingReference, decimal total, CancellationToken cancellationToken)
    {
        using var client = _erpApi!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(LocalTokens.ErpAudience, "norvia-test-carrier-cost-logic-app", [CarrierCostRecordRole], SigningKey));
        var cost = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["carrierBookingNumber"] = bookingNumber,
            ["carrierBookingReference"] = bookingReference,
            ["shipmentNumber"] = "SH-" + bookingNumber[3..],
            ["transportOrderNumber"] = "TO-" + bookingNumber[3..],
            ["carrierId"] = CarrierId,
            ["carrierCost"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = total,
                ["chargeLines"] = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = total }),
                ["source"] = new JsonObject
                {
                    ["kind"] = "CarrierRateCard",
                    ["rateCardVersion"] = new JsonObject { ["rateCardId"] = "CRC-0001", ["version"] = 1 },
                },
                ["calculatedAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            },
            ["deliveryTime"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        using var response = await client.PostAsync(new Uri("/v1/carrier-costs", UriKind.Relative), new StringContent(cost.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>What the ERP holds of a Carrier's invoice, as Finance's list shows it; null when it holds none.</summary>
    public async Task<global::Norvia.Erp.Application.CarrierInvoices.CarrierInvoiceView?> InvoiceInErpAsync(string carrierId, string invoiceNumber, CancellationToken cancellationToken)
    {
        await using var scope = _erpWeb!.Services.CreateAsyncScope();
        var invoices = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new global::Norvia.Erp.Application.CarrierInvoices.List.ListCarrierInvoicesQuery(), cancellationToken);
        return invoices.Select(item => item.Invoice).FirstOrDefault(invoice => invoice.CarrierId == carrierId && invoice.InvoiceNumber == invoiceNumber);
    }

    /// <summary>A Persona's token as the Partner portal issues it locally.</summary>
    private static string PersonaToken(string clientId, string[] roles) => Token(LocalTokens.PartnerApiAudience, clientId, roles, SigningKey);

    private static string Token(string audience, string clientId, string[] roles, byte[] key) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        });

    private static bool IsUnneeded(IResource resource) =>
        resource.Name is "dbgate"
        || resource is ProjectResource && resource.Name != "erp-migrations"
        || resource.GetType().Name.Contains("ServiceBus", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Storage", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Blob", StringComparison.Ordinal);

    /// <summary>The ERP billing API in the tests' process, trusting the key the tokens are signed with.</summary>
    private sealed class ErpApiFactory(string connectionString) : WebApplicationFactory<ErpApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:erp", connectionString);
            builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", LocalTokens.Issuer);
            builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", LocalTokens.ErpAudience);
            builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", LocalTokens.Issuer);
            builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", SigningKeyBase64);
        }
    }

    /// <summary>The ERP UI's process, whose outbox publishes to a stand-in for Service Bus that keeps the event types.</summary>
    private sealed class ErpWebFactory(string connectionString, ConcurrentQueue<string> published) : WebApplicationFactory<ErpWebProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:erp", connectionString);
            builder.UseSetting("ConnectionStrings:messaging", "Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=unused;SharedAccessKey=dW51c2Vk");
            builder.UseSetting("Seed:Path", Path.Combine(AppContext.BaseDirectory, "norvia.seed.json"));
            builder.UseSetting("LocalUser:Name", "Finance (test)");
            builder.UseSetting("LocalUser:Roles:0", "Finance");
            builder.UseSetting("Outbox:PollingInterval", "00:00:00.050");
            builder.ConfigureTestServices(services =>
            {
                var serviceBus = Substitute.For<ServiceBusClient>();
                serviceBus.CreateSender(Arg.Any<string>()).Returns(_ =>
                {
                    var sender = Substitute.For<ServiceBusSender>();
                    sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(sent =>
                    {
                        published.Enqueue(sent.Arg<ServiceBusMessage>().Subject);
                        return Task.CompletedTask;
                    });
                    return sender;
                });
                services.AddSingleton(serviceBus);
            });
        }
    }

    /// <summary>The Carrier invoice worker, configured as the AppHost configures it, calling the ERP billing API in memory.</summary>
    private sealed class InvoiceWorkerFactory(Func<HttpMessageHandler> erpApi) : WebApplicationFactory<InvoiceWorkerProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("CarrierInvoice:ErpBaseAddress", "http://erp-api");
            builder.UseSetting("CarrierInvoice:ErpTokens:Issuer", LocalTokens.Issuer);
            builder.UseSetting("CarrierInvoice:ErpTokens:Audience", LocalTokens.ErpAudience);
            builder.UseSetting("CarrierInvoice:ErpTokens:ClientId", LocalTokens.CarrierInvoiceLogicAppClientId);
            builder.UseSetting("CarrierInvoice:ErpTokens:SigningKey", SigningKeyBase64);
            builder.UseSetting("CarrierInvoice:Trigger:Issuer", LocalTokens.Issuer);
            builder.UseSetting("CarrierInvoice:Trigger:Audience", LocalTokens.CarrierInvoiceTriggerAudience);
            builder.UseSetting("CarrierInvoice:Trigger:ClientId", LocalTokens.ApiManagementClientId);
            builder.UseSetting("CarrierInvoice:Trigger:SigningKey", SigningKeyBase64);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<ErpCarrierInvoices>().ConfigurePrimaryHttpMessageHandler(erpApi));
        }
    }

    /// <summary>The Partner API's stand-in, configured as the AppHost configures it for the Carrier Personas, calling the Carrier invoice worker in memory.</summary>
    private sealed class PartnerApiFactory(Func<HttpMessageHandler> worker) : WebApplicationFactory<PartnerApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:messaging", "Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=unused;SharedAccessKey=dW51c2Vk");
            builder.UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["PartnerApi:SubscriptionKeys:van-dijk-transport"] = SubscriptionKey,
                ["PartnerApi:SubscriptionKeys:stranger-transport"] = "stranger-subscription-key",
                ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = "brouwer-subscription-key",
                ["PartnerApi:ShippersByClientId:" + ShipperClientId] = "SHP-0001",
                ["PartnerApi:CarriersByClientId:" + CarrierClientId] = CarrierId,
                ["PartnerApi:CarriersByClientId:" + StrangerClientId] = "CAR-9999",
                ["PartnerApi:Tokens:Issuer"] = LocalTokens.Issuer,
                ["PartnerApi:Tokens:Audience"] = LocalTokens.PartnerApiAudience,
                ["PartnerApi:Tokens:SigningKey"] = SigningKeyBase64,
                ["Tms:BaseAddress"] = "http://tms-api",
                ["Tms:Tokens:Issuer"] = LocalTokens.Issuer,
                ["Tms:Tokens:Audience"] = LocalTokens.TmsAudience,
                ["Tms:Tokens:ClientId"] = LocalTokens.IntakeLogicAppClientId,
                ["Tms:Tokens:SigningKey"] = SigningKeyBase64,
                ["Tms:AnswerClientId"] = LocalTokens.AnswerLogicAppClientId,
                ["Tms:ShipperTrackingClientId"] = LocalTokens.ApiManagementClientId,
                ["CarrierInvoice:BaseAddress"] = "http://carrier-invoice-worker",
                ["CarrierInvoice:Tokens:Issuer"] = LocalTokens.Issuer,
                ["CarrierInvoice:Tokens:Audience"] = LocalTokens.CarrierInvoiceTriggerAudience,
                ["CarrierInvoice:Tokens:ClientId"] = LocalTokens.ApiManagementClientId,
                ["CarrierInvoice:Tokens:SigningKey"] = SigningKeyBase64,
            })
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<CarrierInvoiceLogicApp>().ConfigurePrimaryHttpMessageHandler(worker);
            });
        }
    }
}
