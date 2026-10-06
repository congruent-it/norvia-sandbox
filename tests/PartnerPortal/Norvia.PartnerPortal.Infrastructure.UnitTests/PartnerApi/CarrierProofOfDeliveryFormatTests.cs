using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierProofOfDeliveryFormatTests
{
    private static readonly CarrierProofOfDeliveryFile Cmr = new("cmr.pdf", "application/pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray());

    private static CarrierProofOfDelivery Proof(
        ProofOfDeliveryCondition condition = ProofOfDeliveryCondition.Clean, string? remark = null, string country = "NLD", params CarrierProofOfDeliveryFile[] files) => new(
        "doc-1", "VDT-77812", "CB-0000042", "J. Jansen", new DateTimeOffset(2030, 7, 2, 10, 15, 30, TimeSpan.Zero), "Utrecht", "3511 AW", country,
        condition, remark, files.Length == 0 ? [Cmr] : files);

    [Fact]
    public void ToJson_IsTheCarrierProofOfDeliveryFormat_WithItsFilesBase64Encoded_WithoutNamingTheCarrier()
    {
        var json = CarrierProofOfDeliveryFormat.ToJson(Proof());

        JsonNode.DeepEquals(json, JsonNode.Parse("""
            {
              "bookingRef": "VDT-77812", "carrierBookingNumber": "CB-0000042", "documentId": "doc-1", "receivedBy": "J. Jansen",
              "deliveryTime": "2030-07-02T12:15:30", "place": "Utrecht", "postcode": "3511 AW", "country": "NLD", "condition": "CLEAN",
              "files": [ { "name": "cmr.pdf", "contentType": "application/pdf", "content": "JVBERi0xLjQKJSVFT0YK" } ]
            }
            """)).Should().BeTrue(json.ToJsonString());
        Contracts.CarrierProofOfDeliverySchemaErrors(json).Should().BeEmpty();
        json.ToJsonString().Should().NotContain("CAR-");
    }

    [Fact]
    public void ToJson_WithReservations_CarriesTheRemark()
    {
        var json = CarrierProofOfDeliveryFormat.ToJson(Proof(ProofOfDeliveryCondition.Reservations, "Two pallets damaged"));

        json["condition"]!.GetValue<string>().Should().Be("RESERVATIONS");
        json["remark"]!.GetValue<string>().Should().Be("Two pallets damaged");
        Contracts.CarrierProofOfDeliverySchemaErrors(json).Should().BeEmpty();
    }

    [Fact]
    public void ToJson_WithoutARemark_LeavesItOut() =>
        CarrierProofOfDeliveryFormat.ToJson(Proof()).ContainsKey("remark").Should().BeFalse();

    [Fact]
    public void ToJson_ManyFiles_AreAllInTheOneBody()
    {
        var json = CarrierProofOfDeliveryFormat.ToJson(Proof(files: [Cmr, new("photo.png", "image/png", [0x89, 0x50])]));

        json["files"]!.AsArray().Select(file => file!["name"]!.GetValue<string>()).Should().Equal("cmr.pdf", "photo.png");
        Contracts.CarrierProofOfDeliverySchemaErrors(json).Should().BeEmpty();
    }

    [Theory]
    [InlineData("NLD", "2030-07-02T12:15:30")]
    [InlineData("GBR", "2030-07-02T11:15:30")]
    [InlineData("FIN", "2030-07-02T13:15:30")]
    public void ToJson_TheDeliveryTimeIsLocalToTheCountry(string country, string local) =>
        CarrierProofOfDeliveryFormat.ToJson(Proof(country: country))["deliveryTime"]!.GetValue<string>().Should().Be(local);

    [Fact]
    public void ToJson_WithoutAProof_Throws()
    {
        var format = () => CarrierProofOfDeliveryFormat.ToJson(null!);

        format.Should().Throw<ArgumentNullException>();
    }
}
