using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        var json = CarrierFormats.Proof(new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "VDT-SIM1-3", "CB-0000042", "VDT-SIM1-3-POD", "Simulated consignee", At, Antwerp, false, null));

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
        var json = CarrierFormats.Proof(new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "R", "CB-0000042", "D", "Someone", At, Rotterdam, true, "Two pallets short"));

        Contracts.CarrierProofSchemaErrors(json).Should().BeEmpty();
        json["condition"]!.GetValue<string>().Should().Be("RESERVATIONS");
        json["remark"]!.GetValue<string>().Should().Be("Two pallets short");
    }

    [Fact]
    public void Proof_TheFile_IsAPdfWhoseContentIsValidBase64()
    {
        var file = CarrierFormats.Proof(new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "R", "CB-0000042", "D", "Someone", At, Rotterdam, false, null))["files"]![0]!;

        file["contentType"]!.GetValue<string>().Should().Be("application/pdf");
        file["name"]!.GetValue<string>().Should().EndWith(".pdf");
        Convert.FromBase64String(file["content"]!.GetValue<string>()).Should().StartWith("%PDF"u8.ToArray());
    }

    [Fact]
    public void Proof_TheFile_IsAOnePagePdfThatReadsLikeADeliveryReceipt()
    {
        var text = ProofText(new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "VDT-SIM1-3", "CB-0000042", "VDT-SIM1-3-POD", "Simulated consignee", At, Rotterdam, false, null));

        text.Should().Contain("Van Dijk Transport").And.Contain("SHP-0001").And.Contain("TO-0001").And.Contain("VDT-SIM1-3")
            .And.Contain("2026-10-13 09:30").And.Contain("Simulated consignee").And.Contain("Clean");
        text.Should().NotContain("Reservations");
    }

    [Fact]
    public void Proof_TheFileWithReservations_ShowsTheRemark()
    {
        var text = ProofText(new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "VDT-SIM1-3", "CB-0000042", "VDT-SIM1-3-POD", "Simulated consignee", At, Rotterdam, true, "Two pallets (short)"));

        text.Should().Contain("Reservations").And.Contain("Two pallets (short)");
        text.Should().NotContain("Clean");
    }

    [Fact]
    public void Proof_TheFile_IsTheSameForTheSameProofAndSmall()
    {
        var proof = new CarrierProofRequest("Van Dijk Transport", "SHP-0001", "TO-0001", "VDT-SIM1-3", "CB-0000042", "VDT-SIM1-3-POD", "Simulated consignee", At, Rotterdam, true, "Two pallets short");

        PdfBytes(proof).Should().Equal(PdfBytes(proof)).And.HaveCountLessThan(2000);
    }

    private static byte[] PdfBytes(CarrierProofRequest proof) =>
        Convert.FromBase64String(CarrierFormats.Proof(proof)["files"]![0]!["content"]!.GetValue<string>());

    // Parses the PDF the way a viewer does: the xref table's offsets must land on their objects, the trailer names the root,
    // the catalog leads to exactly one page, and the page's content stream holds the text.
    private static string ProofText(CarrierProofRequest proof)
    {
        var pdf = Encoding.Latin1.GetString(PdfBytes(proof));
        pdf.Should().StartWith("%PDF-").And.EndWith("%%EOF\n");

        var startxref = int.Parse(Regex.Match(pdf, @"startxref\n(\d+)\n%%EOF").Groups[1].Value, CultureInfo.InvariantCulture);
        pdf[startxref..].Should().StartWith("xref\n");
        var entries = Regex.Matches(pdf[startxref..], @"(\d{10}) 00000 n \n");
        entries.Should().NotBeEmpty();
        for (var number = 1; number <= entries.Count; number++)
        {
            pdf[int.Parse(entries[number - 1].Groups[1].Value, CultureInfo.InvariantCulture)..].Should().StartWith($"{number} 0 obj");
        }

        Regex.Matches(pdf, @"/Type /Page\b").Should().ContainSingle();
        var stream = Regex.Match(pdf, @"/Length (\d+)>>\nstream\n(.*?)\nendstream", RegexOptions.Singleline);
        stream.Groups[2].Value.Length.Should().Be(int.Parse(stream.Groups[1].Value, CultureInfo.InvariantCulture));
        return string.Concat(Regex.Matches(stream.Groups[2].Value, @"\((?:\\.|[^\\)])*\) Tj").Select(m => Regex.Replace(m.Value[1..^4], @"\\(.)", "$1") + "\n"));
    }
}
