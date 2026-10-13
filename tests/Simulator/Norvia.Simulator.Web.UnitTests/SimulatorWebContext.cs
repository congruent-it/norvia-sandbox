using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Simulator.Application.Operators;

namespace Norvia.Simulator.Web.UnitTests;

/// <summary>Renders the Simulator's components with Fluent UI in place and an operator the test signs in, holding both roles.</summary>
public abstract class SimulatorWebContext : BunitContext
{
    protected SimulatorWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton(Requests);
        Services.AddSingleton(new ConsoleLinks(new Uri("http://console.example:5000")));
        Operator = AddAuthorization();
        Operator.SetAuthorized("Dev Eloper").SetRoles(OperatorRoles.Run, OperatorRoles.Read);
        Operator.SetPolicies(OperatorRoles.Run, OperatorRoles.Read);
    }

    protected BunitAuthorizationContext Operator { get; }

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));

    protected ISimulatorRequests Requests { get; } = Substitute.For<ISimulatorRequests>();
}
