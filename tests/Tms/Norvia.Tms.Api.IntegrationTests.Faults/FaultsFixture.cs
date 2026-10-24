using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.Faults;

/// <summary>
/// The TMS with what its faults play through: the Carrier-tracking and Driver reports workers, the Trip dispatch worker that gives the Driver app its Trips, the Driver app's API and the
/// second Service Bus emulator, which holds the tracking queue. The tests put the Carrier's tracking message on the queue and act as the Driver through the Driver app's action API, so each
/// fault plays through the real path. The presenter panel's token, which holds <c>Demo.Fault</c> on both the TMS and the Driver app, switches the faults.
/// The Driver reports worker's retries are short, so a fault of a few seconds is over before it gives up, and the Carrier-tracking worker holds an abandoned message for a second before the
/// queue delivers it again, so the queue's 10 deliveries last longer than a fault of a few seconds (they bound how long a message can wait, and it would use them up in a fraction of a second otherwise).
/// A fault is on for the whole application, so the tests run one at a time.
/// </summary>
public sealed class FaultsFixture : TmsFixture
{
    public const string FaultRole = "Demo.Fault";
    public const string TrackingQueue = "carrier-tracking";
    public const string TestDriver = "DRV-0001";
    public const string SecondTestDriver = "DRV-0002";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private readonly string _seedPath = Path.Combine(Path.GetTempPath(), $"tms.faults-seed.{Guid.NewGuid():N}.json");
    private ServiceBusClient? _edgeBus;

    /// <summary>The presenter panel, as it calls the TMS: the portal's identity, holding the Demo.Fault role.</summary>
    public HttpClient TmsPresenter { get; private set; } = null!;

    /// <summary>The presenter panel, as it calls the Driver app's API.</summary>
    public HttpClient DriverAppPresenter { get; private set; } = null!;

    /// <summary>The second Service Bus emulator, which holds the tracking queue the Carrier-tracking Logic App reads.</summary>
    public ServiceBusClient EdgeBus => _edgeBus!;

    public override async ValueTask DisposeAsync()
    {
        TmsPresenter?.Dispose();
        DriverAppPresenter?.Dispose();
        if (_edgeBus is not null)
        {
            await _edgeBus.DisposeAsync();
        }

        await base.DisposeAsync();
        File.Delete(_seedPath);
        GC.SuppressFinalize(this);
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        TmsPresenter = CreateTmsClient(IssueToken([FaultRole], clientId: LocalTokens.PartnerPortalClientId));
        DriverAppPresenter = DriverAppClient(IssueToken([FaultRole], audience: LocalTokens.DriverAppAudience, clientId: LocalTokens.PartnerPortalClientId));
        await WaitForHealthyAsync("driver-app-api", CancellationToken.None);
        await WaitForHealthyAsync("tracking-worker", CancellationToken.None);
        await WaitForHealthyAsync("driver-reports-worker", CancellationToken.None);
        await WaitForHealthyAsync("trip-dispatch-worker", CancellationToken.None);
        _edgeBus = new ServiceBusClient(await ConnectionStringAsync("messaging-2", CancellationToken.None));
    }

    /// <summary>Restarts the TMS API, as a deployment does: its database is kept, its faults, which live in its process, are not.</summary>
    public Task RestartAsync(CancellationToken cancellationToken) => RestartTmsAsync(cancellationToken);

    /// <summary>A client for the Driver app's API that sends the given token. The Driver's own token has the Driver ID as its one app role.</summary>
    public HttpClient DriverAppClient(string? token)
    {
        var client = App.CreateHttpClient("driver-app-api");
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public HttpClient Driver(string driverId) => DriverAppClient(IssueToken([driverId], audience: LocalTokens.DriverAppAudience, clientId: driverId));

    /// <summary>The state of one fault on the application that owns it.</summary>
    public static async Task<JsonElement> StatusAsync(HttpClient presenter, string fault, CancellationToken cancellationToken) =>
        (await presenter.GetFromJsonAsync<List<JsonElement>>("/v1/faults", cancellationToken))!.Single(status => status.GetProperty("name").GetString() == fault);

    /// <summary>Switches a fault on, as the presenter panel does. Disposing switches it off, so no test leaves a fault on for the next.</summary>
    public static async Task<IAsyncDisposable> SwitchedOnAsync(HttpClient presenter, string fault, TimeSpan duration, CancellationToken cancellationToken)
    {
        await SwitchAsync(presenter, fault, on: true, duration, cancellationToken);
        return new SwitchedOn(presenter, fault);
    }

    public static async Task SwitchAsync(HttpClient presenter, string fault, bool on, TimeSpan duration, CancellationToken cancellationToken)
    {
        using var response = await presenter.PutAsJsonAsync($"/v1/faults/{fault}", new { on, durationSeconds = (int)Math.Ceiling(duration.TotalSeconds) }, cancellationToken);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Waits until the fault has expired by itself.</summary>
    public static async Task WaitUntilOffAsync(HttpClient presenter, string fault, CancellationToken cancellationToken) =>
        await Eventually.UntilAsync(async () => !(await StatusAsync(presenter, fault, cancellationToken)).GetProperty("on").GetBoolean(), cancellationToken);

    /// <summary>The message in the tracking queue's dead-letter queue that the test is looking for, or null when there is none within the wait.</summary>
    public async Task<ServiceBusReceivedMessage?> DeadLetteredAsync(Func<ServiceBusReceivedMessage, bool> isTheOne, TimeSpan wait, CancellationToken cancellationToken)
    {
        await using var receiver = EdgeBus.CreateReceiver(TrackingQueue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        try
        {
            return await Eventually.Async(async () => (await receiver.PeekMessagesAsync(100, cancellationToken: cancellationToken)).FirstOrDefault(isTheOne), cancellationToken, wait);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheTmsFaultTestsDontUse();

        var tracking = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "tracking-worker"));
        tracking.WithEnvironment("Tracking__RedeliveryDelay", RetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture));

        var driverReports = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "driver-reports-worker"));
        driverReports.WithEnvironment("DriverReports__Retry__Count", "10")
            .WithEnvironment("DriverReports__Retry__Interval", RetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment("DriverReports__Retry__MaximumInterval", RetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The test seed, with a shift at the weekend for the two Drivers the tests use, so a Trip can be planned for any day the tests run on.</summary>
    protected override Dictionary<string, string> TmsApiSettings()
    {
        TestSeed.WriteWithWeekendShifts(_seedPath, TestDriver, SecondTestDriver);

        var settings = base.TmsApiSettings();
        settings["Seed__Path"] = _seedPath;
        return settings;
    }

    private sealed class SwitchedOn(HttpClient presenter, string fault) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await SwitchAsync(presenter, fault, on: false, TimeSpan.Zero, CancellationToken.None);
    }
}
