extern alias PortalWeb;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Norvia.AppHost;
using Norvia.PartnerPortal.Web.IntegrationTests.TrackingAssistant;

namespace Norvia.PartnerPortal.Web.UiTests.TrackingAssistant;

/// <summary>
/// Runs the AppHost once for the whole run with the Agents switched on, the TMS, the Partner API's stand-in and what they need, without the UIs. Each test starts a Partner portal of its own on a real
/// port in the tests' process, with the AppHost's own settings and the test's on top: the assistant's limits are counted in the portal's memory, so no test spends another's questions, and a test
/// can take the model away to show an assistant that is unavailable. A headless Chromium drives it as a Shipper Persona.
/// </summary>
public sealed class TrackingAssistantUiFixture : IAsyncLifetime
{
    // What the tests don't use only costs startup time and memory.
    private static readonly string[] Unneeded = ["dbgate", "tms-web", "driver-app-web", "simulator-web", "simulator-migrations"];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private readonly string _tokenSigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IReadOnlyDictionary<string, string> _settings = new Dictionary<string, string>();

    /// <summary>The TMS API, called as the intake Logic App calls it, to enter the Transport Orders a test asks about.</summary>
    public TmsIntake Tms { get; private set; } = null!;

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
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}", "Agents:Enabled=true"], startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        // The AppHost's portal stays stopped: each test runs its own, with the same settings, so it reaches the portal's own seams.
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
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Starts a Partner portal on a port of its own, with the AppHost's settings and the test's on top of them, and the model it runs the assistant on if the test takes the scripted fake away.</summary>
    public PortalHost StartPortal(IReadOnlyDictionary<string, string>? settings = null, IChatClient? model = null) =>
        new(new Dictionary<string, string>(_settings).Concat(settings ?? new Dictionary<string, string>()).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value), model);

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        Tms?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>The Partner portal in the tests' process, listening on a port of its own for a browser.</summary>
    public sealed class PortalHost : IAsyncDisposable
    {
        private readonly Factory _factory;

        internal PortalHost(IReadOnlyDictionary<string, string> settings, IChatClient? model)
        {
            _factory = new Factory(settings, model);
            _factory.UseKestrel(0);
            _factory.StartServer();
            using var probe = _factory.CreateClient();
            Address = probe.BaseAddress!;
        }

        public Uri Address { get; }

        public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

        private sealed class Factory(IReadOnlyDictionary<string, string> settings, IChatClient? model) : WebApplicationFactory<PortalWeb::Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Development");
                foreach (var (name, value) in settings)
                {
                    builder.UseSetting(name, value);
                }

                if (model is not null)
                {
                    builder.ConfigureServices(services => services.AddSingleton(model));
                }
            }
        }
    }
}
