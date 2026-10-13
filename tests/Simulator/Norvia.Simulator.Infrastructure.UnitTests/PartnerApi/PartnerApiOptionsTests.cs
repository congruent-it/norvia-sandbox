using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Worlds;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class PartnerApiOptionsTests
{
    private static readonly PartnerApiOptions Options = new()
    {
        PersonaClientIds = "brouwer-dranken=client-1; tuincentrum-groenrijk=client-3",
        SubscriptionKeys = new() { ["brouwer-dranken"] = "key-1" },
    };

    [Fact]
    public void ClientIdOf_APersonaInTheList_GivesItsClientId()
    {
        Options.ClientIdOf("tuincentrum-groenrijk").Should().Be("client-3");
    }

    [Fact]
    public void ClientIdOf_APersonaNotInTheList_Throws()
    {
        var act = () => Options.ClientIdOf("nobody");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nobody*");
    }

    [Fact]
    public void SubscriptionKeyOf_APersonaWithoutOne_Throws()
    {
        var act = () => Options.SubscriptionKeyOf("tuincentrum-groenrijk");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EveryPersonaCanCall_APersonaTheSimulatorBooksAsWithoutAKey_FailsStartup()
    {
        var world = Substitute.For<IWorldSource>();
        world.Load().Returns(TestPlans.World with
        {
            Shippers = [.. TestPlans.World.Shippers, TestPlans.World.Shippers[0] with { PersonaId = "tuincentrum-groenrijk" }],
        });

        var result = new EveryPersonaCanCall(world).Validate(null, Options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("tuincentrum-groenrijk");
    }

    [Fact]
    public void EveryPersonaCanCall_EveryPersonaHasBoth_Succeeds()
    {
        var world = Substitute.For<IWorldSource>();
        world.Load().Returns(TestPlans.World);

        new EveryPersonaCanCall(world).Validate(null, Options).Succeeded.Should().BeTrue();
    }
}
