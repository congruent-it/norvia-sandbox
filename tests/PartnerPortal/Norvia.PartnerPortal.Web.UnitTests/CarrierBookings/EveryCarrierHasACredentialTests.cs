using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class EveryCarrierHasACredentialTests
{
    private readonly EveryCarrierHasACredential _validation;

    public EveryCarrierHasACredentialTests()
    {
        var personas = Substitute.For<IPersonaDirectory>();
        personas.All.Returns(
        [
            new Persona(new PersonaId("brouwer-dranken"), "Brouwer Dranken", PartyType.Shipper, "SHP-0001", []),
            new Persona(new PersonaId("van-dijk-transport"), "Van Dijk Transport", PartyType.Carrier, "CAR-0001", []),
        ]);
        _validation = new EveryCarrierHasACredential(personas);
    }

    [Fact]
    public void Validate_EveryCarrierHasACredential_Succeeds() =>
        _validation.Validate(null, new CarrierEndpointOptions { ApiKeys = { ["CAR-0001"] = "key-1" } }).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_ACarrierWithoutACredential_FailsNamingIt()
    {
        var result = _validation.Validate(null, new CarrierEndpointOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("CAR-0001");
    }
}
