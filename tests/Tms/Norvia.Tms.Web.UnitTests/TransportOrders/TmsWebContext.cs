using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

/// <summary>Renders TMS UI components with the TMS query side substituted and time under the test's control.</summary>
public abstract class TmsWebContext : BunitContext
{
    protected static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    protected TmsWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Queries);
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = RefreshInterval }));
    }

    protected ITransportOrderQueries Queries { get; } = Substitute.For<ITransportOrderQueries>();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
}
