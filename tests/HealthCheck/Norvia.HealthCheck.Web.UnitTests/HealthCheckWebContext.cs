using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Run;
using Norvia.HealthCheck.Web.Checks;

namespace Norvia.HealthCheck.Web.UnitTests;

/// <summary>Renders the app's components with Fluent UI in place and an operator the test signs in.</summary>
public abstract class HealthCheckWebContext : BunitContext
{
    protected HealthCheckWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        // The Agents are off unless a test turns them on.
        Services.AddOptions<AgentsOptions>();
        Services.AddSingleton(Requests);
        Operator = AddAuthorization();
    }

    protected BunitAuthorizationContext Operator { get; }

    protected IHealthCheckRequests Requests { get; } = Substitute.For<IHealthCheckRequests>();

    protected void TurnAgentsOn() => Services.Configure<AgentsOptions>(options => options.Enabled = true);

    protected static HealthCheckReport AsFound { get; } = new(
        "as-found",
        new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero),
        1,
        [
            new Finding(
                "F-DLQ-ALERT",
                "DLQ-ALERT",
                "A dead-letter queue has no alert",
                "Every Service Bus queue and subscription with a dead-letter queue has an alert on it.",
                Severity.High,
                [new Evidence("carrier-tracking", "No alert watches the dead-letter queue of carrier-tracking."), new Evidence("billing", "No alert <b>watches</b> billing.")],
                "entitiesWithoutDeadLetterAlert",
                "Remove the entity from asFound.entitiesWithoutDeadLetterAlert."),
        ]);

    protected static HealthCheckReport Fixed { get; } = new("fixed", new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), 1, []);
}
