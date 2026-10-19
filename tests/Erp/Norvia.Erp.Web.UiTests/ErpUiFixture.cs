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
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.Erp.Web.UiTests.ErpUiFixture))]

namespace Norvia.Erp.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run, cut down to what Finance's journeys need: SQL Server, the Service Bus emulator, the ERP
/// with its UI and billing API, the Shipper invoice worker that plays the Shipper invoice Logic App (it sends each invoice to the Partner portal
/// and reports it Sent) and the Partner portal, whose invoices inbox is where a Shipper sees what Finance issued. A headless Chromium drives the ERP UI.
/// The tests record their Billable lines through the billing API as the Billing Logic App does, with tokens signed with the key the AppHost trusts.
/// They share one ERP, so each starts by emptying what the ERP records (<see cref="ResetAsync"/>) and keeps its master data.
/// </summary>
public sealed class ErpUiFixture : IAsyncLifetime
{
    private const string RecordRole = "BillableLines.Record";
    private const string ReportRole = "ShipperInvoices.Report";
    private const string ShipperInvoiceWorker = "shipper-invoice-worker";

    // What the invoicing journeys don't use only costs startup time and memory.
    private static readonly string[] Unneeded = ["dbgate", "driver-app-web"];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private string _connectionString = null!;

    /// <summary>The ERP UI, Finance's application.</summary>
    public Uri ErpUi { get; private set; } = null!;

    /// <summary>The Partner portal, where a Shipper Persona's invoices inbox shows what Finance issued.</summary>
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
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        foreach (var resource in new[] { "erp-web", "erp-api", "partner-portal-web" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        await _app.ResourceNotifications.WaitForResourceAsync(ShipperInvoiceWorker, KnownResourceStates.Running, startup.Token);

        ErpUi = _app.GetEndpoint("erp-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");
        _connectionString = await _app.GetConnectionStringAsync("erp", startup.Token)
            ?? throw new InvalidOperationException("The ERP's database has no connection string.");

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
    }

    /// <summary>
    /// Starts a test with the ERP as a fresh one: once the outbox has published all it holds, the records and the outbox are emptied.
    /// The master data stays, and so do the number sequences: the Partner portal keeps the documents it received and ignores a number it has
    /// seen, so numbers must not come round again within the run.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Whatever the last test left unpublished goes out first, so it doesn't arrive in this test.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SettleTimeout);
        while (await ScalarAsync(connection, "SELECT COUNT(*) FROM OutboxMessages WHERE PublishedAt IS NULL", timeout.Token) != 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }

        foreach (var table in new[] { "CreditNotes", "ShipperInvoices", "BillableLines", "OutboxMessages" })
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM {table}";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>Records a Billable line with the ERP billing API, as the Billing Logic App does when the TMS makes a Transport Order Billable.</summary>
    public async Task RecordBillableLineAsync(string transportOrderNumber, string shipperId, decimal freight, decimal minimumChargeAdjustment, CancellationToken cancellationToken)
    {
        var chargeLines = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = freight });
        if (minimumChargeAdjustment > 0)
        {
            chargeLines.Add(new JsonObject { ["type"] = "MinimumChargeAdjustment", ["amount"] = minimumChargeAdjustment });
        }

        var billable = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "TransportOrderBillable",
            ["eventVersion"] = 1,
            ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["intakeCorrelationId"] = Guid.NewGuid().ToString(),
            ["transportOrderNumber"] = transportOrderNumber,
            ["shipperId"] = shipperId,
            ["shipperReference"] = $"REF-{transportOrderNumber}",
            ["shipmentNumber"] = $"SH-{transportOrderNumber[3..]}",
            ["deliveryTime"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O", CultureInfo.InvariantCulture),
            ["freightCharge"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = freight + minimumChargeAdjustment,
                ["chargeLines"] = chargeLines,
                ["rateCardVersion"] = new JsonObject { ["rateCardId"] = "RC-0001", ["version"] = 1 },
            },
            ["releasedWithoutProofOfDelivery"] = false,
        };

        using var client = ApiAs(RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Reports a Shipper invoice as not delivered, as the Shipper invoice Logic App does when the Shipper's invoice endpoint fails.</summary>
    public async Task ReportInvoiceNotDeliveredAsync(string invoiceNumber, CancellationToken cancellationToken)
    {
        using var client = ApiAs(ReportRole);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipper-invoices/{invoiceNumber}/report-not-delivered", UriKind.Relative))
        {
            Content = JsonContent.Create(new { statusCode = 503, message = "The Shipper's invoice endpoint answered 503." }),
        };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request, cancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Stops the Shipper invoice worker, so no invoice is sent or reported Sent until <see cref="StartShipperInvoiceWorkerAsync"/>,
    /// the way a Shipper invoice Logic App that is down leaves an invoice.
    /// </summary>
    public async Task StopShipperInvoiceWorkerAsync(CancellationToken cancellationToken)
    {
        using var stopping = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync(ShipperInvoiceWorker, KnownResourceCommands.StopCommand, stopping.Token);
        // What a stopped project reports varies (Exited, Finished, even Unknown); it is down once it is none of these.
        string[] up = [KnownResourceStates.Starting, KnownResourceStates.Running, KnownResourceStates.Stopping, KnownResourceStates.Waiting];
        await _app.ResourceNotifications.WaitForResourceAsync(
            ShipperInvoiceWorker, resource => resource.Snapshot.State?.Text is { } state && !up.Contains(state), stopping.Token);
    }

    public async Task StartShipperInvoiceWorkerAsync(CancellationToken cancellationToken)
    {
        using var starting = WithinTwoMinutes(cancellationToken);
        await _app!.ResourceCommands.ExecuteCommandAsync(ShipperInvoiceWorker, KnownResourceCommands.StartCommand, starting.Token);
        await _app.ResourceNotifications.WaitForResourceAsync(ShipperInvoiceWorker, KnownResourceStates.Running, starting.Token);
    }

    // A resource that never gets there fails the test, rather than hanging the run.
    private static CancellationTokenSource WithinTwoMinutes(CancellationToken cancellationToken)
    {
        var within = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        within.CancelAfter(TimeSpan.FromMinutes(2));
        return within;
    }

    private HttpClient ApiAs(string role)
    {
        var client = _app!.CreateHttpClient("erp-api");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.ErpAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object>
            {
                ["azp"] = role == RecordRole ? LocalTokens.BillingLogicAppClientId : LocalTokens.ShipperInvoiceLogicAppClientId,
                ["roles"] = new[] { role },
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        }));
        return client;
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
