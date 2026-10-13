extern alias SimulatorWeb;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>The Simulator running in the tests' process, with its settings.</summary>
public sealed class SimulatorHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<SimulatorWeb::Program> _factory;

    internal SimulatorHost(IReadOnlyDictionary<string, string> settings)
    {
        _factory = new SimulatorFactory(settings);
        Requests = _factory.Services.GetRequiredService<ISimulatorRequests>();
    }

    /// <summary>What the Simulator's pages ask of it: configure, plan, run, cancel and the log.</summary>
    public ISimulatorRequests Requests { get; }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private sealed class SimulatorFactory(IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<SimulatorWeb::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in settings)
            {
                builder.UseSetting(name, value);
            }
        }
    }
}
