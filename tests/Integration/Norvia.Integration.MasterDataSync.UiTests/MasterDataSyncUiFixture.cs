using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Norvia.AppHost;
using Norvia.Integration.MasterDataSync.IntegrationTests;

namespace Norvia.Integration.MasterDataSync.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run with what Finance's master-data journeys touch: SQL Server with the ERP's and the TMS's databases, the two Service Bus emulators, the ERP
/// (its UI, which publishes the outbox, and its API, which the UI saves through), the master-data sync worker that plays the E3 Logic App, the TMS (API and UI) and the Integration console.
/// A headless Chromium drives the three UIs. The path is the real one, from a save in the ERP's form to the TMS's copy. Only one hop is changed: the worker is told the TMS is a
/// <see cref="TmsProxy"/>, which forwards each call unless a test has scripted a fault for it.
/// A second ERP UI, <see cref="ErpUiOfAReader"/>, signs in a user who doesn't hold the Finance role. The tests make records with IDs of their own, share the estate and run one at a time.
/// </summary>
public sealed class MasterDataSyncUiFixture : TmsFixture
{
    private const string ReaderHost = "erp-web-reader";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private IPlaywright? _playwright;
    private string _tmsConnection = string.Empty;

    /// <summary>The proxy the worker reaches the TMS through.</summary>
    public TmsProxy Proxy { get; } = new();

    /// <summary>The ERP UI, signed in as Finance.</summary>
    public Uri ErpUi { get; private set; } = null!;

    /// <summary>The same ERP, signed in as a user without the Finance role.</summary>
    public Uri ErpUiOfAReader { get; private set; } = null!;

    public Uri TmsWeb { get; private set; } = null!;

    public Uri IntegrationConsole { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await Proxy.StartAsync();

        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        await base.InitializeAsync();
        Proxy.ForwardTo(App.GetEndpoint("tms-api", "http"));
        foreach (var resource in new[] { "erp-web", "erp-api", "tms-web", "integration-console-web" })
        {
            await WaitForHealthyAsync(resource, CancellationToken.None);
        }

        try
        {
            await WaitForHealthyAsync(ReaderHost, CancellationToken.None);
        }
        catch (DistributedApplicationException exception)
        {
            throw new InvalidOperationException($"The second ERP UI failed to start.{Environment.NewLine}{await LogOfAsync(ReaderHost)}", exception);
        }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        {
            await App.ResourceNotifications.WaitForResourceAsync("master-data-sync-worker", KnownResourceStates.Running, timeout.Token);
        }

        ErpUi = App.GetEndpoint("erp-web", "http");
        ErpUiOfAReader = App.GetEndpoint(ReaderHost, "http");
        TmsWeb = App.GetEndpoint("tms-web", "http");
        IntegrationConsole = App.GetEndpoint("integration-console-web", "http");
        _tmsConnection = await ConnectionStringAsync("tms", CancellationToken.None);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await base.DisposeAsync();
        await Proxy.DisposeAsync();
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheMasterDataSyncUiTestsDontUse();

        // The worker's retry waits are the workflow's 5 s up to 20 s; a test waits for them, so they are short here. The count stays the workflow's three.
        var worker = builder.CreateResourceBuilder(builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "master-data-sync-worker"));
        worker.WithEnvironment("MasterDataSync__TmsBaseAddress", Proxy.Address.ToString())
            .WithEnvironment("MasterDataSync__Retry__Interval", "00:00:00.200")
            .WithEnvironment("MasterDataSync__Retry__MaximumInterval", "00:00:00.800");
        var erp = builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "erp-web");
        builder.CreateResourceBuilder(erp)
            .WithEnvironment("Outbox__PollingInterval", "00:00:00.100")
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);

        AddErpUiOfAReader(builder, erp);
    }

    /// <summary>
    /// A second ERP UI on the same database, set up as the first is but for who it signs in: a user with an app role that isn't Finance. It publishes nothing: its outbox looks only
    /// once a minute, so the first one's outbox alone sends what Finance saves.
    /// </summary>
    private static void AddErpUiOfAReader(IDistributedApplicationTestingBuilder builder, DotnetProjectResource erp)
    {
        // Not AddDotnetProject: Aspire 17.0.0 keeps what it knows of the AppHost's builds per builder, and the testing builder is a wrapper with none of it, so a project added here makes a second
        // build resource of the same name and the startup fails ("already exists"). The projects are built already, as the AppHost's own are, so this one needs no build of its own.
#pragma warning disable CS0618 // AddProject is obsolete for AddDotnetProject, which cannot be used from a test.
        var reader = builder.AddProject(ReaderHost, erp.GetProjectMetadata().ProjectPath);
#pragma warning restore CS0618
        var database = builder.Resources.OfType<IResourceWithConnectionString>().Single(resource => resource.Name == "erp");
        var edge = builder.Resources.OfType<IResourceWithConnectionString>().Single(resource => resource.Name == "messaging-2");
        reader.WithReference(builder.CreateResourceBuilder(database))
            .WithReference(builder.CreateResourceBuilder(edge), "messaging")
            .WithHttpHealthCheck("/health")
            .WithEnvironment("Seed__Path", Path.Combine(AppContext.BaseDirectory, "norvia.seed.json"))
            // The reader saves nothing, so the ERP API it would save through is never called.
            .WithEnvironment("ErpApi__BaseAddress", "http://localhost:1")
            .WithEnvironment("ErpApi__LocalTokens__Issuer", LocalTokens.Issuer)
            .WithEnvironment("ErpApi__LocalTokens__Audience", LocalTokens.ErpAudience)
            .WithEnvironment("ErpApi__LocalTokens__ClientId", LocalTokens.ErpUiClientId)
            .WithEnvironment("ErpApi__LocalTokens__SigningKey", Convert.ToBase64String(new byte[32]))
            .WithEnvironment("LocalUser__Name", "A reader (local)")
            .WithEnvironment("LocalUser__Roles__0", "Reader")
            .WithEnvironment("Outbox__PollingInterval", "00:01:00")
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty)
            .WaitFor(builder.CreateResourceBuilder(erp));
    }

    /// <summary>The TMS keeps the repo's seed file the AppHost gives it, so the Rate Cards and the Fleet are the estate's, the same the ERP loads.</summary>
    protected override Dictionary<string, string> TmsApiSettings()
    {
        var settings = base.TmsApiSettings();
        settings.Remove("Seed__Path");
        return settings;
    }

    private async Task<string> LogOfAsync(string resourceName)
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var loggers = App.Services.GetRequiredService<ResourceLoggerService>();
        var lines = new List<string>();
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await foreach (var batch in loggers.WatchAsync(model.Resources.Single(resource => resource.Name == resourceName)).WithCancellation(window.Token))
            {
                lines.AddRange(batch.Select(line => line.Content));
            }
        }
        catch (OperationCanceledException)
        {
            // What the resource logged so far is what there is.
        }

        return string.Join(Environment.NewLine, lines.TakeLast(40));
    }

    /// <summary>Waits until the TMS holds its copy of the record at the revision or later; fails the test when it never does. The table is the copy's: <c>ShipperCopies</c>, <c>RateCardCopies</c> and so on.</summary>
    public async Task WaitForReplicaAsync(string table, string id, int revision, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WaitTimeout);
        try
        {
            while (await RevisionOfAsync(table, id, timeout.Token) < revision)
            {
                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {WaitTimeout.TotalSeconds:0} s for the TMS's copy of {id} in {table} at revision {revision}.");
        }
    }

    private async Task<int> RevisionOfAsync(string table, string id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_tmsConnection);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Revision FROM {table} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteScalarAsync(ct) is int revision ? revision : 0;
    }
}
