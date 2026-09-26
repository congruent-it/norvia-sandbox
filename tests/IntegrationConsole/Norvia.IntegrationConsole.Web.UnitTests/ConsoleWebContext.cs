using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Norvia.IntegrationConsole.Web.UnitTests;

/// <summary>Renders the console's components with Fluent UI in place and an operator the test signs in.</summary>
public abstract class ConsoleWebContext : BunitContext
{
    protected ConsoleWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Operator = AddAuthorization();
    }

    protected BunitAuthorizationContext Operator { get; }
}
