using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Norvia.HealthCheck.Application.Agents;

namespace Norvia.HealthCheck.Web.UiTests;

/// <summary>
/// The Estate health check on Kestrel, so a browser drives it with nothing else running. It reads the snapshot the test names and signs the
/// local operator in with the roles the test names, as the AppHost does. The Agents are on, and the Agent itself is the
/// <see cref="IAgentClient"/> fake that a test scripts. Each test starts a host of its own.
/// </summary>
public sealed class HealthCheckWebHost : WebApplicationFactory<Program>
{
    private readonly string _snapshot;
    private readonly string _costSnapshot;
    private readonly string[] _roles;

    /// <summary>
    /// The day the spend page reads as today. The cost snapshots hold 1 to 14 November 2026, so with this day the two compared weeks are
    /// 1 to 7 and 8 to 14 November, however long after that the test runs.
    /// </summary>
    public static readonly DateTimeOffset Today = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    /// <param name="snapshot">The estate the app reads: <c>as-found</c> or <c>fixed</c>.</param>
    /// <param name="roles">The app roles the local operator holds.</param>
    public HealthCheckWebHost(string snapshot, params string[] roles)
        : this(snapshot, "fixed", roles)
    {
    }

    /// <param name="snapshot">The estate the app reads: <c>as-found</c> or <c>fixed</c>.</param>
    /// <param name="costSnapshot">The modelled spend the app reads: <c>fixed</c> or <c>chatty-telemetry</c>.</param>
    /// <param name="roles">The app roles the local operator holds.</param>
    public HealthCheckWebHost(string snapshot, string costSnapshot, params string[] roles)
    {
        _snapshot = snapshot;
        _costSnapshot = costSnapshot;
        _roles = roles;
        UseKestrel(0);
    }

    /// <summary>The Agent, for a test to say what it answers.</summary>
    public IAgentClient Agent { get; } = Substitute.For<IAgentClient>();

    /// <summary>Where the app listens.</summary>
    public Uri Address =>
        new(Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The local operator signs in only in Development, and Development is where the host reads the build's static web assets.
        builder.UseEnvironment("Development");
        builder.UseSetting("Estate:Snapshot", _snapshot);
        builder.UseSetting("Estate:CostSnapshot", _costSnapshot);
        builder.UseSetting("LocalOperator:Name", "Iris Operator");
        for (var index = 0; index < _roles.Length; index++)
        {
            builder.UseSetting($"LocalOperator:Roles:{index}", _roles[index]);
        }

        builder.ConfigureServices(services =>
        {
            // Through the options, not the setting: the setting would also map the agent API, which needs tokens of its own.
            services.Configure<AgentsOptions>(options => options.Enabled = true);
            services.RemoveAll<IAgentClient>();
            services.AddSingleton(Agent);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Today));
        });
    }
}
