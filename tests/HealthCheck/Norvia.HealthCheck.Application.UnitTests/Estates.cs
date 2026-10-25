using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.UnitTests;

internal static class Estates
{
    public static EstateConfiguration Fixed { get; } = new("fixed", [new("carrier-tracking", true), new("billing", true)]);

    public static EstateConfiguration WithoutAlerts(params string[] names) =>
        new("as-found", [.. names.Select(name => new ServiceBusEntity(name, false)), new ServiceBusEntity("billing", true)]);
}
