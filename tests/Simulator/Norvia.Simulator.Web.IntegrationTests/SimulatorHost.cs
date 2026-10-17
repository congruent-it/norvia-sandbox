extern alias SimulatorWeb;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>The Simulator running in the tests' process, with its settings.</summary>
public sealed class SimulatorHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<SimulatorWeb::Program> _factory;

    internal SimulatorHost(IReadOnlyDictionary<string, string> settings)
    {
        Telemetry = new CapturedTelemetry();
        _factory = new SimulatorFactory(settings, Telemetry);
        Requests = _factory.Services.GetRequiredService<ISimulatorRequests>();
    }

    /// <summary>What the Simulator's pages ask of it: configure, plan, run, cancel and the log.</summary>
    public ISimulatorRequests Requests { get; }

    /// <summary>What this Simulator logged and traced since it started.</summary>
    public CapturedTelemetry Telemetry { get; }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Telemetry.Dispose();
    }

    private sealed class SimulatorFactory(IReadOnlyDictionary<string, string> settings, CapturedTelemetry telemetry) : WebApplicationFactory<SimulatorWeb::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddProvider(telemetry));
            foreach (var (name, value) in settings)
            {
                builder.UseSetting(name, value);
            }
        }
    }
}
