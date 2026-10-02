using System.Text;
using Norvia.PartnerPortal.Domain.CarrierBookings;
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
                ("van-dijk-transport", "Van Dijk Transport", PartyType.Carrier, "CAR-0001"),
                ("kessler-spedition", "Kessler Spedition", PartyType.Carrier, "CAR-0002"),
                ("pakketpunt-express", "PakketPunt Express", PartyType.Carrier, "CAR-0003"));
    }

    [Theory]
    [InlineData("van-dijk-transport", "VDT", CarrierAnswerMode.Manual)]
    [InlineData("kessler-spedition", "KES", CarrierAnswerMode.AutoConfirm)]
    [InlineData("pakketpunt-express", "PKT", CarrierAnswerMode.AutoConfirm)]
    public void Find_ACarrierPersona_HasItsReferencePrefix_AndDefaultAnswerMode(string persona, string prefix, CarrierAnswerMode mode)
    {
        var carrier = TheSeedFile().Find(new PersonaId(persona))!.Carrier!;

        carrier.BookingReferencePrefix.Should().Be(prefix);
        carrier.DefaultAnswerPolicy.Mode.Should().Be(mode);
        carrier.DefaultAnswerPolicy.Delay.Should().BePositive();
    }

    [Fact]
    public void Find_AShipperPersona_HasNoCarrierProfile() =>
        TheSeedFile().Find(new PersonaId("brouwer-dranken"))!.Carrier.Should().BeNull();

    [Fact]
    public void New_ACarrierPersonaWithoutItsCarrierSection_Throws()
    {
        var read = () => Read("""
            { "shippers": [], "carriers": [{ "id": "CAR-0001" }],
              "personas": [{ "id": "ghost", "displayName": "Ghost", "party": { "type": "Carrier", "id": "CAR-0001" } }] }
            """);

        read.Should().Throw<InvalidDataException>().WithMessage("*ghost*carrier*");
    }

    [Fact]
    public void New_ACarrierPersonaWithoutAReferencePrefix_Throws()
    {
        var read = () => Read("""
            { "shippers": [], "carriers": [{ "id": "CAR-0001" }],
              "personas": [{ "id": "ghost", "displayName": "Ghost", "party": { "type": "Carrier", "id": "CAR-0001" },
                "carrier": { "bookingReferencePrefix": " ", "answer": { "mode": "Manual", "rejectionReason": "NoCapacity", "delaySeconds": 5 } } }] }
            """);

        read.Should().Throw<InvalidDataException>().WithMessage("*prefix*");
    }

    [Fact]
    public void Find_AShipperPersona_HasItsShippersAddressBook()
    {
        var brouwer = TheSeedFile().Find(new PersonaId("brouwer-dranken"))!;

        brouwer.SavedAddresses.Should().HaveCount(4);
        brouwer.SavedAddresses[0].Should().Be(new SavedAddress("Brouwer Dranken warehouse", "Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL", Consignee: null));
        brouwer.SavedAddresses[1].Consignee.Should().Be(new SavedConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"));
        brouwer.SavedAddresses[2].Should().Be(new SavedAddress(
            "Drankenhandel De Schelde", "Noorderlaan 120", "2030", "Antwerpen", "BE",
            new SavedConsignee("Drankenhandel De Schelde BV", "Pieter Janssens", "+32 3 555 0117")));
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
