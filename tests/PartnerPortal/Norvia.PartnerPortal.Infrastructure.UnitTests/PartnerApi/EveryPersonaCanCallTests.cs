using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class EveryPersonaCanCallTests
{
    private readonly EveryPersonaCanCall _validation;

    public EveryPersonaCanCallTests()
    {
        var personas = Substitute.For<IPersonaDirectory>();
        personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _validation = new EveryPersonaCanCall(personas);
    }

    [Fact]
    public void Validate_EveryPersonaHasAClientIdAndAKey_Succeeds()
    {
        var result = _validation.Validate(null, new PartnerApiOptions
        {
            PersonaClientIds = "brouwer-dranken=client-1;van-dijk-transport=client-2",
            SubscriptionKeys = new() { ["brouwer-dranken"] = "key-1", ["van-dijk-transport"] = "key-2" },
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_APersonaWithoutAClientIdOrAKey_FailsNamingIt()
    {
        var result = _validation.Validate(null, new PartnerApiOptions
        {
            PersonaClientIds = "brouwer-dranken=client-1",
            SubscriptionKeys = new() { ["van-dijk-transport"] = "key-2" },
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2)
            .And.Contain(failure => failure.Contains("subscription key", StringComparison.Ordinal) && failure.Contains("brouwer-dranken", StringComparison.Ordinal))
            .And.Contain(failure => failure.Contains("client ID", StringComparison.Ordinal) && failure.Contains("van-dijk-transport", StringComparison.Ordinal));
    }
}
