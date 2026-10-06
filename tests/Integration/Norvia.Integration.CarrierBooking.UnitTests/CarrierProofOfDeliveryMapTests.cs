using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CarrierProofOfDeliveryMapTests
{
    private const string CarrierId = "CAR-0001";

    private static MappedProofOfDelivery Map(string json) => CarrierProofOfDeliveryMap.Map(json, CarrierId);

    private static string Message(string deliveryTime = "2030-10-02T08:45:00", string country = "NLD", string condition = "CLEAN", string extra = "", string files = """[{ "name": "cmr.pdf", "contentType": "application/pdf", "content": "aGVsbG8=" }]""") =>
        $$"""
        { "bookingRef": "R-1", "documentId": "D-1", "receivedBy": "A. Jansen", "deliveryTime": "{{deliveryTime}}",
          "place": "Rotterdam", "postcode": "3011 AA", "country": "{{country}}", "condition": "{{condition}}",
          "files": {{files}} {{extra}} }
        """;

    [Theory]
    [InlineData("CLEAN", "Clean")]
    [InlineData("RESERVATIONS", "Reservations")]
    public void Map_BothConditions_MapToTheirCanonicalName(string condition, string expected)
    {
        var extra = condition == "RESERVATIONS" ? """, "remark": "Dented" """ : "";

        Map(Message(condition: condition, extra: extra)).Metadata["condition"]!.GetValue<string>().Should().Be(expected);
    }

    [Theory]
    [InlineData("2030-01-15T10:00:00", "NLD", "2030-01-15T10:00:00+01:00")]
    [InlineData("2030-07-15T10:00:00", "NLD", "2030-07-15T10:00:00+02:00")]
    [InlineData("2030-07-15T10:00:00", "GBR", "2030-07-15T10:00:00+01:00")]
    public void Map_ALocalDeliveryTime_IsReadInTheLocationsTimeZone(string local, string country, string expected)
    {
        Map(Message(deliveryTime: local, country: country)).Metadata["deliveryTime"]!.GetValue<string>().Should().Be(expected);
    }

    [Fact]
    public void Map_TheCountry_ConvertsToAlpha2AndTheLocationIsNested()
    {
        var location = Map(Message()).Metadata["location"]!.AsObject();

        location.ToJsonString().Should().Be("""{"place":"Rotterdam","postcode":"3011 AA","countryCode":"NL"}""");
    }

    [Fact]
    public void Map_TheCarrier_IsTheOneFromTheTokenNeverThePayload()
    {
        var metadata = Map(Message(extra: """, "carrierId": "CAR-0002" """)).Metadata;

        metadata["source"]!.ToJsonString().Should().Be("""{"type":"Carrier","carrierId":"CAR-0001"}""");
        metadata.ContainsKey("carrierId").Should().BeFalse();
    }

    [Fact]
    public void Map_TheFiles_AreDecodedAndDescribedInOrder()
    {
        var mapped = Map(Message(files: """[{ "name": "a.pdf", "contentType": "application/pdf", "content": "aGVsbG8=" }, { "name": "b.png", "contentType": "image/png", "content": "d29ybGQ=" }]"""));

        mapped.Files.Select(file => (file.Name, file.ContentType, Encoding.ASCII.GetString(file.Content)))
            .Should().Equal(("a.pdf", "application/pdf", "hello"), ("b.png", "image/png", "world"));
        mapped.Metadata["files"]!.ToJsonString().Should().Be("""[{"name":"a.pdf","contentType":"application/pdf"},{"name":"b.png","contentType":"image/png"}]""");
    }

    [Fact]
    public void Map_TheMetadata_CarriesNoFileContent()
    {
        Map(Message()).Metadata.ToJsonString().Should().NotContain("aGVsbG8=");
    }

    [Theory]
    [InlineData("""{ "bookingRef": "R-1" }""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Map_ContentThatIsNoProofOfDelivery_Fails(string json)
    {
        var act = () => Map(json);

        act.Should().Throw<InvalidProofOfDeliveryException>();
    }

    [Theory]
    [InlineData("bookingRef")]
    [InlineData("documentId")]
    [InlineData("receivedBy")]
    [InlineData("deliveryTime")]
    [InlineData("place")]
    [InlineData("postcode")]
    [InlineData("country")]
    [InlineData("condition")]
    [InlineData("files")]
    public void Map_AMissingRequiredField_Fails(string field)
    {
        var message = JsonNode.Parse(Message())!.AsObject();
        message.Remove(field);

        var act = () => Map(message.ToJsonString());

        act.Should().Throw<InvalidProofOfDeliveryException>().WithMessage($"*{field}*");
    }

    [Theory]
    [InlineData("deliveryTime", "10-12-2030 10:00")]
    [InlineData("country", "XXX")]
    [InlineData("condition", "clean")]
    [InlineData("condition", "DAMAGED")]
    public void Map_AValueItCantRead_Fails(string field, string value)
    {
        var message = JsonNode.Parse(Message())!.AsObject();
        message[field] = value;

        var act = () => Map(message.ToJsonString());

        act.Should().Throw<InvalidProofOfDeliveryException>().WithMessage($"*{field}*");
    }

    [Fact]
    public void Map_ReservationsWithoutARemark_Fails()
    {
        var act = () => Map(Message(condition: "RESERVATIONS"));

        act.Should().Throw<InvalidProofOfDeliveryException>().WithMessage("*remark*");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{ "name": "a.pdf", "contentType": "application/pdf", "content": "***" }]""")]
    [InlineData("""[{ "name": "a.pdf", "contentType": "application/pdf" }]""")]
    [InlineData("""[{ "contentType": "application/pdf", "content": "aGVsbG8=" }]""")]
    [InlineData("""[{ "name": "a.pdf", "content": "aGVsbG8=" }]""")]
    [InlineData("\"aGVsbG8=\"")]
    public void Map_FilesItCantRead_Fail(string files)
    {
        var act = () => Map(Message(files: files));

        act.Should().Throw<InvalidProofOfDeliveryException>().WithMessage("*file*");
    }

    [Fact]
    public void Map_MoreThanFiveFiles_Fails()
    {
        var file = """{ "name": "a.pdf", "contentType": "application/pdf", "content": "aGVsbG8=" }""";

        var act = () => Map(Message(files: $"[{string.Join(",", Enumerable.Repeat(file, 6))}]"));

        act.Should().Throw<InvalidProofOfDeliveryException>().WithMessage("*file*");
    }

    [Fact]
    public void Map_OptionalBookingNumberAndRemark_AreCopiedWhenGiven()
    {
        var metadata = Map(Message(extra: """, "carrierBookingNumber": "CB-0000042", "remark": "Left at the gate" """)).Metadata;

        metadata["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000042");
        metadata["remark"]!.GetValue<string>().Should().Be("Left at the gate");
        metadata["carrierBookingReference"]!.GetValue<string>().Should().Be("R-1");
        metadata["documentId"]!.GetValue<string>().Should().Be("D-1");
        metadata["receivedBy"]!.GetValue<string>().Should().Be("A. Jansen");
    }

    [Fact]
    public void Map_WithoutACarrierId_Fails()
    {
        var act = () => CarrierProofOfDeliveryMap.Map(Message(), " ");

        act.Should().Throw<ArgumentException>();
    }
}
