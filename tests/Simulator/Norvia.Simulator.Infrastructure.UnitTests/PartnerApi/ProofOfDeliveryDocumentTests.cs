using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class ProofOfDeliveryDocumentTests
{
    private static readonly DateTime DeliveredAt = new(2026, 10, 13, 9, 30, 0, DateTimeKind.Unspecified);
    private static readonly PlannedAddress Rotterdam = new("Coolsingel 1", "3011 AD", "Rotterdam", "NL");

    [Fact]
    public void Create_IsAPdfThatAViewerCanParse_WithOnePage()
    {
        var pdf = Pdf(Proof());

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
    }

    [Fact]
    public void Create_ACleanDelivery_ReadsLikeADeliveryReceipt()
    {
        var lines = Lines(Proof());

        lines.Should().Equal(
            "Proof of delivery",
            "Van Dijk Transport",
            "Shipment: SHP-0001",
            "Transport Order: TO-0001",
            "Carrier booking reference: REF-BOOKING-7",
            "Delivered: 2026-10-13 09:30",
            "Received by: Simulated consignee",
            "Condition: Clean");
    }

    [Fact]
    public void Create_WithReservations_ShowsTheConditionAndTheRemark()
    {
        var lines = Lines(Proof(reservations: true, remark: "Two pallets (short)"));

        lines.Should().Contain("Condition: Reservations").And.Contain("Remark: Two pallets (short)");
        lines.Should().NotContain("Condition: Clean");
    }

    [Fact]
    public void Create_TheSameProof_GivesTheSameBytes()
    {
        ProofOfDeliveryDocument.Create(Proof(), DeliveredAt).Should().Equal(ProofOfDeliveryDocument.Create(Proof(), DeliveredAt));
    }

    [Fact]
    public void Create_ADifferentDeliveryTime_ChangesNothingButThatLine()
    {
        var first = Lines(Proof(), DeliveredAt);
        var later = Lines(Proof(), DeliveredAt.AddHours(2));

        first.Zip(later).Where(pair => pair.First != pair.Second).Should().ContainSingle().Which.Second.Should().Be("Delivered: 2026-10-13 11:30");
    }

    [Fact]
    public void Create_IsSmall_ThroughTheClaimCheck()
    {
        ProofOfDeliveryDocument.Create(Proof(reservations: true, remark: "Two pallets short"), DeliveredAt).Length.Should().BeLessThan(2000);
    }

    [Fact]
    public void Create_TextWithBracketsBackslashesAndLineBreaks_StaysOneReadableLine()
    {
        var lines = Lines(Proof(receivedBy: "A (B) \\ C\nD"));

        lines.Should().Contain("Received by: A (B) \\ C D");
    }

    private static CarrierProofRequest Proof(bool reservations = false, string? remark = null, string receivedBy = "Simulated consignee") =>
        new("Van Dijk Transport", "SHP-0001", "TO-0001", "REF-BOOKING-7", "CB-0000042", "DOC-9", receivedBy, DeliveredAt, Rotterdam, reservations, remark);

    private static string Pdf(CarrierProofRequest proof, DateTime? at = null) => Encoding.Latin1.GetString(ProofOfDeliveryDocument.Create(proof, at ?? DeliveredAt));

    // The text of the page's content stream, one entry per shown line.
    private static List<string> Lines(CarrierProofRequest proof, DateTime? at = null) =>
        [.. Regex.Matches(Pdf(proof, at), @"\(((?:\\.|[^\\)])*)\) Tj").Select(match => Regex.Replace(match.Groups[1].Value, @"\\(.)", "$1"))];
}
