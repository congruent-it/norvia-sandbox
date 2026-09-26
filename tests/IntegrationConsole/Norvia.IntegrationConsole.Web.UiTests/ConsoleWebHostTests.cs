using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Web.UiTests;

public sealed class ConsoleWebHostTests(ConsoleUiFixture console)
{
    [Fact]
    public void TheHost_ServesEveryPortFromItsFake()
    {
        var ports = typeof(OperatorRoles).Assembly.GetTypes().Where(type => type.IsInterface);

        foreach (var port in ports)
        {
            console.Console.Services.GetRequiredService(port).Should().BeSameAs(console.Console.Ports[port], $"{port.Name} is a port");
        }

        console.Console.Ports.Keys.Should().BeEquivalentTo(ports);
    }
}
