using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperNotifications;

public sealed class EveryShipperHasACredentialTests
{
    private readonly EveryShipperHasACredential _validation;

    public EveryShipperHasACredentialTests()
    {
        var personas = Substitute.For<IPersonaDirectory>();
        personas.All.Returns(
        [
            new Persona(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "SHP-0001", []),
            new Persona(new PersonaId("tuincentrum-groenrijk"), "Tuincentrum Groenrijk", PartyType.Shipper, "SHP-0003", []),
            new Persona(new PersonaId("van-dijk-transport"), "Van Dijk Transport", PartyType.Carrier, "CAR-0001", []),
        ]);
        _validation = new EveryShipperHasACredential(personas);
    }

    [Fact]
    public void Validate_EveryShipperHasACredential_Succeeds()
    {
        var result = _validation.Validate(null, new ShipperEndpointOptions { ApiKeys = { ["SHP-0001"] = "key-1", ["SHP-0003"] = "key-3" } });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_AShipperWithoutACredential_FailsNamingIt_AndIgnoresCarriers()
    {
        var result = _validation.Validate(null, new ShipperEndpointOptions { ApiKeys = { ["SHP-0001"] = "key-1", ["SHP-0003"] = " " } });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("SHP-0003").And.Contain("Tuincentrum Groenrijk");
    }
}
