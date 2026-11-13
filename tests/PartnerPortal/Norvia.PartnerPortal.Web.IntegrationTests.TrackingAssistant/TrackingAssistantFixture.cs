extern alias PortalWeb;

using System.Collections.Concurrent;
using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Norvia.AppHost;
using Norvia.PartnerPortal.TestSupport;

[assembly: AssemblyFixture(typeof(Norvia.PartnerPortal.Web.IntegrationTests.TrackingAssistant.TrackingAssistantFixture))]

namespace Norvia.PartnerPortal.Web.IntegrationTests.TrackingAssistant;

/// <summary>
/// Runs the AppHost once for the whole test run with the Agents switched on (as a developer does with <c>Agents:Enabled</c>), the TMS, the Partner API's stand-in and what they need, without the UIs. The assistant's tests ask through a Partner portal in the
/// tests' process, started with the AppHost's own settings, so the portal calls the real Partner API stand-in with the Persona's own token and subscription key and the real TMS
/// answers from the Transport Orders each test enters for itself. Each test starts a portal of its own (<see cref="StartPortal"/>), because the assistant's limits are counted in the
/// portal's memory: no test spends another's questions.
/// </summary>
public sealed class TrackingAssistantFixture : IAsyncLifetime
{
    /// <summary>The Shipper Persona that asks: Brouwer Dranken, Shipper SHP-0001.</summary>
    public const string AskingPersona = "brouwer-dranken";

    public const string AskingShipperId = "SHP-0001";

    /// <summary>A second Shipper Persona, whose Transport Orders exist in the TMS and must never reach the first one's input: Tuincentrum Groenrijk, Shipper SHP-0003.</summary>
    public const string OtherPersona = "tuincentrum-groenrijk";

    public const string OtherShipperId = "SHP-0003";

    // What the assistant's tests don't use only costs startup time and memory.
    private static readonly string[] ResourcesTheTestsDontNeed = ["dbgate", "tms-web", "driver-app-web", "simulator-web", "simulator-migrations"];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private readonly string _tokenSigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentQueue<Activity> _spans = new();
    private ActivityListener? _listener;
    private DistributedApplication? _app;
    private IReadOnlyDictionary<string, string> _settings = new Dictionary<string, string>();

    /// <summary>The TMS API, called as the intake Logic App calls it, to enter the Transport Orders a test asks about.</summary>
    public TmsIntake Tms { get; private set; } = null!;

    /// <summary>The AppHost's settings for the Partner portal, which every test's portal starts from.</summary>
    public IReadOnlyDictionary<string, string> PortalSettings => _settings;

    /// <summary>The spans the portal's process finished, so a test reads the run's span by its run ID.</summary>
    public IReadOnlyCollection<Activity> Spans => _spans;

    public async ValueTask InitializeAsync()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);

        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}", "Agents:Enabled=true"], startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => ResourcesTheTestsDontNeed.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        // The AppHost's portal stays stopped: each test runs its own, with the same settings, so it reaches the portal's doors and the run's own seams.
        var portal = builder.Resources.Single(resource => resource.Name == "partner-portal-web");
        builder.CreateResourceBuilder((ProjectResource)portal).WithExplicitStart();

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("partner-portal-migrations", KnownResourceStates.Finished, startup.Token);
        foreach (var resource in new[] { "tms-api", "partner-api" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        var configuration = await ExecutionConfigurationBuilder.Create(portal)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run) { Services = _app.Services }), cancellationToken: startup.Token);
        var settings = configuration.EnvironmentVariables
            .Where(setting => setting.Key.Contains("__", StringComparison.Ordinal))
            .ToDictionary(setting => setting.Key.Replace("__", ":", StringComparison.Ordinal), setting => setting.Value);
        settings["Seed:Path"] = Path.Combine(AppContext.BaseDirectory, "norvia.seed.json");
        _settings = settings;

        Tms = new TmsIntake(_app.CreateHttpClient("tms-api"), _tokenSigningKey);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Tms?.Dispose();
        _listener?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Starts a Partner portal in the tests' process with the AppHost's settings and the test's own on top of them.</summary>
    public AssistantPortal StartPortal(IReadOnlyDictionary<string, string>? settings = null) =>
        new(new Dictionary<string, string>(_settings).Concat(settings ?? new Dictionary<string, string>()).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value));

    /// <summary>The span of the assistant run with this run ID.</summary>
    public Activity RunSpan(string runId) =>
        _spans.Single(span => span.OperationName == "Ask tracking assistant" && span.GetTagItem("runId") as string == runId);
}
