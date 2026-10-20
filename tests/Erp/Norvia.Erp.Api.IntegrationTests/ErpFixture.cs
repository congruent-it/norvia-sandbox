extern alias ErpApi;
extern alias ErpWeb;

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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;
using NSubstitute;
using ErpApiProgram = ErpApi::Program;
using ErpWebProgram = ErpWeb::Program;

[assembly: AssemblyFixture(typeof(Norvia.Erp.Api.IntegrationTests.ErpFixture))]

namespace Norvia.Erp.Api.IntegrationTests;

/// <summary>
/// Runs the AppHost once for the whole test run, cut down to SQL Server and the ERP's migrations, and starts the ERP in the tests' process against that
/// database: its billing API, and its UI's process, which holds Finance's actions, the monthly Invoice run and the outbox publisher. Service Bus is
/// the ERP's one outbound dependency and is replaced at the edge: the messages the outbox publishes are kept for the tests to read. Both processes
/// read one clock the tests set, and the API trusts the key the fixture signs its callers' tokens with, as Entra would sign them.
/// The tests share the database, so each starts by emptying what the ERP records (<see cref="ResetAsync"/>) and keeps its master data.
/// </summary>
public sealed class ErpFixture : IAsyncLifetime
{
    /// <summary>The app role only the Billing Logic App's managed identity holds.</summary>
    public const string RecordRole = "BillableLines.Record";

    /// <summary>The app role only the Shipper invoice Logic App's managed identity holds.</summary>
    public const string ReportRole = "ShipperInvoices.Report";

    /// <summary>The app role only the Carrier cost Logic App's managed identity holds.</summary>
    public const string RecordCarrierCostRole = "CarrierCosts.Record";

    /// <summary>The app role only the Carrier invoice Logic App's managed identity holds, for the Carrier invoices it receives.</summary>
    public const string ReceiveCarrierInvoiceRole = "CarrierInvoices.Receive";

    /// <summary>The app role only the Carrier invoice outcome Logic App's managed identity holds, for its reports on what it told the Carrier.</summary>
    public const string ReportCarrierInvoiceRole = "CarrierInvoices.Report";

    /// <summary>The app role only the Simulator's identity holds, for the Finance decisions on Carrier invoice lines it plays.</summary>
    public const string ManageCarrierInvoiceRole = "CarrierInvoices.Manage";

    /// <summary>How often the ERP's outbox looks for a message to publish; quick, so tests don't wait for it.</summary>
    private static readonly TimeSpan OutboxPollingInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the outbox runs after a message arrives, a few cycles, so a duplicate or a message that shouldn't be sent would show up.</summary>
    private static readonly TimeSpan OutboxSettleTime = OutboxPollingInterval * 8;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly string SigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private readonly ConcurrentQueue<PublishedMessage> _published = new();
    private DistributedApplication? _app;
    private string _connectionString = string.Empty;
    private ApiFactory? _api;
    private WebFactory? _web;

    /// <summary>The time the ERP sees. A test sets it; each starts at <see cref="DefaultNow"/>.</summary>
    public SettableClock Clock { get; } = new(DefaultNow);

    /// <summary>Monday 19 October 2026, 10:00 in Amsterdam: well inside a month, so no monthly run is due.</summary>
    public static DateTimeOffset DefaultNow { get; } = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Finance's side of the ERP: its UI's actions, its monthly run and its lists.</summary>
    public Finance Finance { get; private set; } = null!;

    /// <summary>Everything the ERP's outbox has published since the test began, as the Service Bus messages it sent.</summary>
    public IReadOnlyList<PublishedMessage> Published => [.. _published];

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>([], startup.Token);

        // The ERP's records are all the tests need: its database and the migrations that make it. The applications, Service Bus and storage only cost startup time and memory.
        foreach (var resource in builder.Resources.Where(IsUnneeded).ToList())
        {
            builder.Resources.Remove(resource);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("erp-migrations", KnownResourceStates.Finished, startup.Token);

        _connectionString = await _app.GetConnectionStringAsync("erp", startup.Token)
            ?? throw new InvalidOperationException("The ERP's database has no connection string.");

        // The UI's process goes first: it loads the master data the API checks Shippers against.
        _web = new WebFactory(_connectionString, Clock, _published);
        Finance = new Finance(_web.Services, Clock);
        _api = new ApiFactory(_connectionString, Clock);
        _ = _api.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_web is not null)
        {
            await _web.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>A client of the billing API holding the app roles, as a managed identity holding them calls.</summary>
    public HttpClient ApiAs(params string[] roles) => ApiWith(IssueToken(roles));

    /// <summary>A client of the billing API that sends the token as it is, or none.</summary>
    public HttpClient ApiWith(string? token)
    {
        var client = _api!.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>A token the billing API trusts, for a caller holding the roles; <paramref name="audience"/> and <paramref name="signingKey"/> can be set wrong to test its checks.</summary>
    public static string IssueToken(string[] roles, string audience = LocalTokens.ErpAudience, string? signingKey = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = "norvia-test-logic-app", ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)), SecurityAlgorithms.HmacSha256),
        });

    /// <summary>
    /// Starts a test with the ERP as a fresh one: once the outbox has published all it holds, the records, sequences and outbox are emptied, the messages kept
    /// are forgotten and the clock is set to <see cref="DefaultNow"/>. The master data stays.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Whatever the last test left unpublished goes out first, so it doesn't arrive in this test.
        await WaitUntilAsync(async () => await ScalarAsync(connection, "SELECT COUNT(*) FROM OutboxMessages WHERE PublishedAt IS NULL", cancellationToken) == 0, cancellationToken);

        foreach (var table in new[] { "CarrierInvoices", "CarrierCosts", "CreditNotes", "ShipperInvoices", "BillableLines", "CreditNoteNumberSequences", "InvoiceNumberSequences", "OutboxMessages" })
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM {table}";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        _published.Clear();
        Clock.Set(DefaultNow);
    }

    /// <summary>The messages published so far, once the outbox has run a few cycles since the last arrived, so a duplicate or a message that shouldn't be sent would show up.</summary>
    public async Task<IReadOnlyList<PublishedMessage>> PublishedSettledAsync(CancellationToken cancellationToken)
    {
        var count = -1;
        while (count != _published.Count)
        {
            count = _published.Count;
            await Task.Delay(OutboxSettleTime, cancellationToken);
        }

        return Published;
    }

    /// <summary>Waits for the outbox to publish a message that fits, and returns it; fails the test when none arrives.</summary>
    public async Task<PublishedMessage> PublishedAsync(Func<PublishedMessage, bool> fits, CancellationToken cancellationToken)
    {
        PublishedMessage? found = null;
        await WaitUntilAsync(() =>
        {
            found = Published.FirstOrDefault(fits);
            return Task.FromResult(found is not null);
        }, cancellationToken);
        return found!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitTimeout);
        while (!await condition())
        {
            try
            {
                await Task.Delay(OutboxPollingInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"What the test waited for didn't happen in {WaitTimeout}.");
            }
        }
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static bool IsUnneeded(IResource resource) =>
        resource.Name is "dbgate"
        || resource is ProjectResource && resource.Name != "erp-migrations"
        || resource.GetType().Name.Contains("ServiceBus", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Storage", StringComparison.Ordinal)
        || resource.GetType().Name.Contains("Blob", StringComparison.Ordinal);

    /// <summary>The ERP billing API in the tests' process.</summary>
    private sealed class ApiFactory(string connectionString, TimeProvider clock) : WebApplicationFactory<ErpApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:erp", connectionString);
            builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", LocalTokens.Issuer);
            builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", LocalTokens.ErpAudience);
            builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", LocalTokens.Issuer);
            // JWT bearer configuration takes a key as base64.
            builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(Encoding.UTF8.GetBytes(SigningKey)));
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }

    /// <summary>The ERP UI's process in the tests' process, as the Finance user, with its outbox publishing to a stand-in for Service Bus.</summary>
    private sealed class WebFactory(string connectionString, TimeProvider clock, ConcurrentQueue<PublishedMessage> published) : WebApplicationFactory<ErpWebProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:erp", connectionString);
            builder.UseSetting("ConnectionStrings:messaging", "Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=unused;SharedAccessKey=dW51c2Vk");
            builder.UseSetting("Seed:Path", Path.Combine(AppContext.BaseDirectory, "norvia.seed.json"));
            builder.UseSetting("LocalUser:Name", "Finance (test)");
            builder.UseSetting("LocalUser:Roles:0", "Finance");
            builder.UseSetting("Outbox:PollingInterval", OutboxPollingInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clock);

                // Service Bus is outside the ERP: the sender keeps what the outbox sends, as a topic's subscribers would receive it.
                var serviceBus = Substitute.For<ServiceBusClient>();
                serviceBus.CreateSender(Arg.Any<string>()).Returns(call =>
                {
                    var topic = call.Arg<string>();
                    var sender = Substitute.For<ServiceBusSender>();
                    sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).Returns(sent =>
                    {
                        published.Enqueue(PublishedMessage.From(topic, sent.Arg<ServiceBusMessage>()));
                        return Task.CompletedTask;
                    });
                    return sender;
                });
                services.AddSingleton(serviceBus);
            });
        }
    }
}

/// <summary>A time the tests set. Its timers are the system's, so the ERP's workers keep their own pace.</summary>
public sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    private long _utcTicks = now.UtcTicks;

    public void Set(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
}

/// <summary>A message the ERP's outbox published, as a subscriber would read it.</summary>
public sealed record PublishedMessage(string Topic, string EventType, string MessageId, string CorrelationId, JsonObject Body, IReadOnlyDictionary<string, object> Properties)
{
    public static PublishedMessage From(string topic, ServiceBusMessage message) => new(
        topic,
        message.Subject,
        message.MessageId,
        message.CorrelationId,
        JsonNode.Parse(message.Body.ToString())!.AsObject(),
        message.ApplicationProperties.ToDictionary(property => property.Key, property => property.Value));

    /// <summary>The number of the invoice or Credit note the message is about; the tracking contract's <c>documentNumber</c>.</summary>
    public string? DocumentNumber => Properties.TryGetValue("documentNumber", out var number) ? number as string : null;
}

/// <summary>Finance's side of the ERP: what its UI does, which the tests drive through the ERP UI's own actions, and what its lists show.</summary>
public sealed class Finance
{
    private readonly IServiceProvider _services;
    private readonly SettableClock _clock;

    internal Finance(IServiceProvider services, SettableClock clock)
    {
        _services = services;
        _clock = clock;
    }

    /// <summary>Finance's <b>Invoice now</b> for a Shipper.</summary>
    public async Task<ActionResult> InvoiceNowAsync(string shipperId, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ErpWeb::Norvia.Erp.Web.ShipperInvoices.InvoiceNowAction>().RunAsync(shipperId, cancellationToken);
        return outcome switch
        {
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.InvoiceNowOutcome.Issued issued => new ActionResult(Number: issued.InvoiceNumber, LineCount: issued.LineCount),
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.InvoiceNowOutcome.Failed failed => new ActionResult(Refusal: failed.Reason),
            _ => new ActionResult(),
        };
    }

    /// <summary>Finance's <b>Credit note</b> on one line of a Shipper invoice.</summary>
    public async Task<ActionResult> IssueCreditNoteAsync(string invoiceNumber, string transportOrderNumber, decimal amount, string? reason, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ErpWeb::Norvia.Erp.Web.ShipperInvoices.IssueCreditNoteAction>()
            .RunAsync(invoiceNumber, transportOrderNumber, amount, reason, cancellationToken);
        return outcome switch
        {
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.IssueCreditNoteOutcome.Issued issued => new ActionResult(Number: issued.CreditNoteNumber),
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.IssueCreditNoteOutcome.Refused refused => new ActionResult(Refusal: refused.Reason),
            _ => new ActionResult(),
        };
    }

    /// <summary>Finance's <b>Send again</b> for a Not delivered Shipper invoice or Credit note.</summary>
    public async Task<ActionResult> SendAgainAsync(string documentNumber, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ErpWeb::Norvia.Erp.Web.ShipperInvoices.SendAgainAction>().RunAsync(documentNumber, cancellationToken);
        return outcome switch
        {
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.SendAgainOutcome.SentAgain sent => new ActionResult(Number: sent.InvoiceNumber),
            ErpWeb::Norvia.Erp.Web.ShipperInvoices.SendAgainOutcome.Refused refused => new ActionResult(Refusal: refused.Reason),
            _ => new ActionResult(),
        };
    }

    /// <summary>
    /// The monthly Invoice run at the time: a worker of its own looks whether a run is due, as the ERP's does when it starts and every minute, so
    /// each call is the first look of a freshly started ERP at that time.
    /// </summary>
    public async Task RunMonthlyAt(DateTimeOffset now, CancellationToken cancellationToken)
    {
        _clock.Set(now);
        var run = _services.GetRequiredService<ErpWeb::Norvia.Erp.Web.ShipperInvoices.MonthlyInvoiceRun>();
        var worker = new ErpWeb::Norvia.Erp.Web.ShipperInvoices.MonthlyInvoiceRunWorker(run, _clock);
        await worker.RunIfDueAsync(cancellationToken);
    }

    /// <summary>A command of the ERP sent as the UI's actions send it, for the stable code of a refusal the actions only word.</summary>
    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command, cancellationToken);
    }

    /// <summary>What Finance's lists show: asks the ERP the query through its own mediator.</summary>
    public async Task<TResult> AskAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}

/// <summary>What an action of Finance did: the number of the document it issued, how many lines it covered, or why it refused. All empty when there was nothing to do.</summary>
public sealed record ActionResult(string? Number = null, int LineCount = 0, string? Refusal = null)
{
    public bool Refused => Refusal is not null;
}
