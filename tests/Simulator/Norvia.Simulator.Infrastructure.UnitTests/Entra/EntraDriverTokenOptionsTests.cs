using Norvia.Simulator.Infrastructure.Entra;

namespace Norvia.Simulator.Infrastructure.UnitTests.Entra;

public sealed class EntraDriverTokenOptionsTests
{
    private static readonly EntraDriverTokenOptions Options = new() { DriverClientIds = "DRV-0001=client-1; DRV-0002=client-2" };

    [Fact]
    public void ClientIdOf_ADriverInTheList_GivesTheirClientApp()
    {
        Options.ClientIdOf("DRV-0002").Should().Be("client-2");
    }

    [Fact]
    public void ClientIdOf_ADriverNotInTheList_Throws()
    {
        var act = () => Options.ClientIdOf("DRV-0009");

        act.Should().Throw<InvalidOperationException>().WithMessage("*DRV-0009*");
    }
}
