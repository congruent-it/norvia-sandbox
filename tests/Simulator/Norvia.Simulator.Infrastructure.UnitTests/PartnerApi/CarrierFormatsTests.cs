using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierFormatsTests
{
    // 09:30 in Rotterdam (UTC+2 until 25 October 2026).
    private static readonly DateTimeOffset At = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);

    private static readonly PlannedAddress Rotterdam = new("Coolsingel 1", "3011 AD", "Rotterdam", "NL");
    private static readonly PlannedAddress Antwerp = new("Meir 5", "2000", "Antwerpen", "BE");
    private static readonly PlannedAddress Luxembourg = new("Rue 3", "1111", "Luxembourg", "LU");
    private static readonly PlannedAddress Berlin = new("Straße 1", "10115", "Berlin", "DE");

    [Fact]
    public void Answer_AConfirmation_KeepsToTheCarrierAnswerSchema()
    {
        var json = CarrierFormats.Answer(new CarrierAnswerRequest("CB-0000042", true, "VDT-SIM1-3", At, null));

        Contracts.CarrierAnswerSchemaErrors(json).Should().BeEmpty();
        json["decision"]!.GetValue<string>().Should().Be("CONFIRMED");
        json["bookingRef"]!.GetValue<string>().Should().Be("VDT-SIM1-3");
        json["plannedCollection"]!.GetValue<string>().Should().Be("2026-10-13T07:30:00+00:00");
        json.ContainsKey("declineReason").Should().BeFalse();
    }

    [Fact]
    public void Answer_AConfirmationWithoutAPlannedCollection_LeavesItOut()
    {
        var json = CarrierFormats.Answer(new CarrierAnswerRequest("CB-0000042", true, "VDT-SIM1-3", null, null));

        Contracts.CarrierAnswerSchemaErrors(json).Should().BeEmpty();
        json.ContainsKey("plannedCollection").Should().BeFalse();
    }

    [Fact]
    public void Answer_ADecline_KeepsToTheCarrierAnswerSchemaAndNamesTheReason()
    {
        var json = CarrierFormats.Answer(new CarrierAnswerRequest("CB-0000042", false, null, null, "NO_CAPACITY"));

        Contracts.CarrierAnswerSchemaErrors(json).Should().BeEmpty();
        json["decision"]!.GetValue<string>().Should().Be("DECLINED");
        json["declineReason"]!.GetValue<string>().Should().Be("NO_CAPACITY");
        json.ContainsKey("bookingRef").Should().BeFalse();
    }

    [Fact]
    public void Answer_NeverNamesTheCarrier()
    {
        CarrierFormats.Answer(new CarrierAnswerRequest("CB-0000042", true, "R", null, null)).Select(pair => pair.Key)
            .Should().NotContain(key => key.Contains("carrierId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(CarrierEvent.Collected, "PU")]
    [InlineData(CarrierEvent.Departed, "DEP")]
    [InlineData(CarrierEvent.Arrived, "ARR")]
    [InlineData(CarrierEvent.OutForDelivery, "OFD")]
    [InlineData(CarrierEvent.Delivered, "POD")]
    [InlineData(CarrierEvent.CollectionFailed, "NPU")]
    [InlineData(CarrierEvent.DeliveryFailed, "NDL")]
    [InlineData(CarrierEvent.Damaged, "DMG")]
    public void Tracking_EveryEvent_KeepsToTheCarrierTrackingSchema(CarrierEvent carrierEvent, string code)
    {
        var json = CarrierFormats.Tracking(new CarrierTrackingRequest("VDT-SIM1-3", "CB-0000042", $"VDT-SIM1-3-{code}", carrierEvent, At, Rotterdam, null));

        Contracts.CarrierTrackingSchemaErrors(json).Should().BeEmpty();
        json["statusCode"]!.GetValue<string>().Should().Be(code);
        json["eventId"]!.GetValue<string>().Should().Be($"VDT-SIM1-3-{code}");
        json["bookingRef"]!.GetValue<string>().Should().Be("VDT-SIM1-3");
        json["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000042");
    }

    [Theory]
    [InlineData("NL", "NLD", "2026-10-13T09:30:00")]
    [InlineData("BE", "BEL", "2026-10-13T09:30:00")]
    [InlineData("LU", "LUX", "2026-10-13T09:30:00")]
    [InlineData("DE", "DEU", "2026-10-13T09:30:00")]
    public void Tracking_TheEventTime_IsLocalAtThePlaceWithoutAnOffsetAndTheCountryIsAlpha3(string alpha2, string alpha3, string local)
    {
        var place = alpha2 switch { "NL" => Rotterdam, "BE" => Antwerp, "LU" => Luxembourg, _ => Berlin };

        var json = CarrierFormats.Tracking(new CarrierTrackingRequest("R", "CB-0000042", "E", CarrierEvent.Arrived, At, place, null));

        json["eventTime"]!.GetValue<string>().Should().Be(local);
        json["country"]!.GetValue<string>().Should().Be(alpha3);
        json["place"]!.GetValue<string>().Should().Be(place.City);
        json["postcode"]!.GetValue<string>().Should().Be(place.Postcode);
    }

    [Fact]
    public void Tracking_ARemark_IsKeptAndNoRemarkIsLeftOut()
    {
        CarrierFormats.Tracking(new CarrierTrackingRequest("R", "CB-0000042", "E", CarrierEvent.Damaged, At, Rotterdam, "A pallet was damaged"))["remark"]!
            .GetValue<string>().Should().Be("A pallet was damaged");
        CarrierFormats.Tracking(new CarrierTrackingRequest("R", "CB-0000042", "E", CarrierEvent.Arrived, At, Rotterdam, null)).ContainsKey("remark").Should().BeFalse();
    }

    [Fact]
    public void Tracking_AnUnknownCountry_IsRefusedByName()
    {
        var act = () => CarrierFormats.Tracking(new CarrierTrackingRequest("R", "CB-0000042", "E", CarrierEvent.Arrived, At, new PlannedAddress("a", "1", "b", "XX"), null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*XX*");
    }

    [Fact]
    public void Proof_ACleanDelivery_KeepsToTheCarrierProofSchema()
    {
        var json = CarrierFormats.Proof(new CarrierProofRequest("VDT-SIM1-3", "CB-0000042", "VDT-SIM1-3-POD", "Simulated consignee", At, Antwerp, false, null));

        Contracts.CarrierProofSchemaErrors(json).Should().BeEmpty();
        json["condition"]!.GetValue<string>().Should().Be("CLEAN");
        json["deliveryTime"]!.GetValue<string>().Should().Be("2026-10-13T09:30:00");
        json["country"]!.GetValue<string>().Should().Be("BEL");
        json["files"]!.AsArray().Should().ContainSingle();
        json.ContainsKey("remark").Should().BeFalse();
    }

    [Fact]
    public void Proof_WithReservations_NeedsAndKeepsItsRemark()
    {
        var json = CarrierFormats.Proof(new CarrierProofRequest("R", "CB-0000042", "D", "Someone", At, Rotterdam, true, "Two pallets short"));

        Contracts.CarrierProofSchemaErrors(json).Should().BeEmpty();
        json["condition"]!.GetValue<string>().Should().Be("RESERVATIONS");
        json["remark"]!.GetValue<string>().Should().Be("Two pallets short");
    }

    [Fact]
    public void Proof_TheFile_IsAPdfWhoseContentIsValidBase64()
    {
        var file = CarrierFormats.Proof(new CarrierProofRequest("R", "CB-0000042", "D", "Someone", At, Rotterdam, false, null))["files"]![0]!;

        file["contentType"]!.GetValue<string>().Should().Be("application/pdf");
        file["name"]!.GetValue<string>().Should().EndWith(".pdf");
        Convert.FromBase64String(file["content"]!.GetValue<string>()).Should().StartWith("%PDF"u8.ToArray());
    }
}
