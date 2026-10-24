using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Web.Agents;

namespace Norvia.IntegrationConsole.Web.UnitTests;

/// <summary>Renders the console's components with Fluent UI in place and an operator the test signs in.</summary>
public abstract class ConsoleWebContext : BunitContext
{
    protected ConsoleWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        // The Agents are off unless a test turns them on.
        Services.AddOptions<AgentsOptions>();
        Services.AddSingleton(Explain);
        Operator = AddAuthorization();
    }

    protected BunitAuthorizationContext Operator { get; }

    protected IExplainRequests Explain { get; } = Substitute.For<IExplainRequests>();

    protected void TurnAgentsOn() => Services.Configure<AgentsOptions>(options => options.Enabled = true);
}
