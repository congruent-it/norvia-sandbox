using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly PersonaId VanDijk = new("van-dijk-transport");

    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", new DateTime(2030, 10, 2, 7, 30, 0), new DateTime(2030, 10, 2, 11, 0, 0)),
        new CarrierBookingStop("Kölner Landstraße 301", "40591", "Düsseldorf", "DEU", null, null),
        "Getränke Schmitz GmbH",
        [new CarrierBookingGoodsLine("pallet", 4, 2400m, 120m, 80m, 160m)]);

    private static CarrierBookingRequest Receive(PersonaId? persona = null, string number = "CB-0000001", DateTimeOffset? at = null) =>
        CarrierBookingRequest.Receive(persona ?? VanDijk, number, "SH-0000042", "release-1", at ?? ReceivedAt, Details);

    [Fact]
    public void Receive_KeepsWhatNorviaAsked()
    {
        var request = Receive();

        request.PersonaId.Should().Be(VanDijk);
        request.CarrierBookingNumber.Should().Be("CB-0000001");
        request.ShipmentNumber.Should().Be("SH-0000042");
        request.CorrelationId.Should().Be("release-1");
        request.ReceivedAt.Should().Be(ReceivedAt);
        request.Details.Should().BeSameAs(Details);
    }

    [Fact]
    public void Receive_GivesEachRequestAnIdOfItsOwn() =>
        Receive().Id.Should().NotBe(Receive().Id);

    [Theory]
    [InlineData("", "SH-1", "corr")]
    [InlineData("CB-1", " ", "corr")]
    [InlineData("CB-1", "SH-1", "")]
    public void Receive_WithoutANumberOrACorrelationId_Throws(string bookingNumber, string shipmentNumber, string correlationId)
    {
        var receive = () => CarrierBookingRequest.Receive(VanDijk, bookingNumber, shipmentNumber, correlationId, ReceivedAt, Details);

        receive.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Receive_WithoutAPersonaOrDetails_Throws()
    {
        ((Action)(() => CarrierBookingRequest.Receive(null!, "CB-1", "SH-1", "corr", ReceivedAt, Details))).Should().Throw<ArgumentNullException>();
        ((Action)(() => CarrierBookingRequest.Receive(VanDijk, "CB-1", "SH-1", "corr", ReceivedAt, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CarrierBookingRequestsOfPersonaSpec_ListsOnlyThatPersonasRequests_LatestFirst()
    {
        var earlier = Receive(number: "CB-1");
        var later = Receive(number: "CB-2", at: ReceivedAt.AddMinutes(5));
        var someoneElses = Receive(new PersonaId("kessler-spedition"), "CB-3");

        var listed = new CarrierBookingRequestsOfPersonaSpec(VanDijk).Evaluate([earlier, someoneElses, later]);

        listed.Should().Equal(later, earlier);
    }

    [Fact]
    public void CarrierBookingRequestByNumberSpec_FindsThePersonasRequestForTheNumber()
    {
        var wanted = Receive(number: "CB-1");
        var otherNumber = Receive(number: "CB-2");
        var otherPersona = Receive(new PersonaId("kessler-spedition"), "CB-1");

        var found = new CarrierBookingRequestByNumberSpec(VanDijk, "CB-1").Evaluate([otherNumber, otherPersona, wanted]);

        found.Should().Equal(wanted);
    }
}
