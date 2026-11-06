using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

/// <summary>
/// Renders TMS UI components with the TMS query side and the TMS API's commands substituted, and time under the test's
/// control.
/// </summary>
public abstract class TmsWebContext : BunitContext
{
    protected static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    protected TmsWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Queries);
        Services.AddSingleton(Commands);
        Services.AddSingleton(Explain);
        Services.AddSingleton(Options.Create(AgentSettings));
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = RefreshInterval }));
    }

    protected ITransportOrderQueries Queries { get; } = Substitute.For<ITransportOrderQueries>();

    protected IExplainRequests Explain { get; } = Substitute.For<IExplainRequests>();

    /// <summary>The Agents are off unless a test turns them on, as they are off unless the AppHost or the estate does.</summary>
    protected AgentsOptions AgentSettings { get; } = new();

    protected ITransportOrderCommands Commands { get; } = Substitute.For<ITransportOrderCommands>();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
}
