using System.Text;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Infrastructure.Personas;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Personas;

public sealed class SeedFilePersonaDirectoryTests
{
    private static SeedFilePersonaDirectory Read(string seed) => new(new MemoryStream(Encoding.UTF8.GetBytes(seed)));

    private static SeedFilePersonaDirectory TheSeedFile() => Read(Contracts.Read("seed/norvia.seed.json"));

    [Fact]
    public void All_IsEveryPersonaInTheSeedFile_InItsOrder()
    {
        TheSeedFile().All.Select(persona => (persona.Id.Value, persona.DisplayName, persona.PartyType, persona.PartyId))
            .Should().Equal(
                ("brouwer-dranken", "Brouwer Dranken", PartyType.Shipper, "SHP-0001"),
                ("van-dijk-transport", "Van Dijk Transport", PartyType.Carrier, "CAR-0001"));
    }

    [Fact]
    public void Find_AShipperPersona_HasItsShippersAddressBook()
    {
        var brouwer = TheSeedFile().Find(new PersonaId("brouwer-dranken"))!;

        brouwer.SavedAddresses.Should().HaveCount(3);
        brouwer.SavedAddresses[0].Should().Be(new SavedAddress("Brouwer Dranken warehouse", "Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL", Consignee: null));
        brouwer.SavedAddresses[1].Consignee.Should().Be(new SavedConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"));
    }

    [Fact]
    public void Find_ACarrierPersona_HasNoAddressBook()
    {
        TheSeedFile().Find(new PersonaId("van-dijk-transport"))!.SavedAddresses.Should().BeEmpty();
    }

    [Fact]
    public void Find_UnknownPersona_IsNull()
    {
        TheSeedFile().Find(new PersonaId("nobody")).Should().BeNull();
    }

    [Fact]
    public void New_PersonaOfAPartyTheSeedFileDoesNotHave_Throws()
    {
        var read = () => Read("""
            { "shippers": [], "carriers": [],
              "personas": [{ "id": "ghost", "displayName": "Ghost", "party": { "type": "Shipper", "id": "SHP-9999" } }] }
            """);

        read.Should().Throw<InvalidDataException>().WithMessage("*SHP-9999*");
    }

    [Fact]
    public void New_EmptySeedFile_Throws()
    {
        var read = () => Read("null");

        read.Should().Throw<InvalidDataException>();
    }
}
