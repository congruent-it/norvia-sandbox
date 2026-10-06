using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierProofOfDeliveryTests
{
    private static readonly CarrierProofOfDeliveryFile Cmr = new("cmr.pdf", "application/pdf", [1, 2, 3]);

    private static readonly CarrierProofOfDeliveryFile[] Files = [Cmr];

    private static CarrierProofOfDelivery Proof(string documentId = "doc-1", ProofOfDeliveryCondition condition = ProofOfDeliveryCondition.Clean) => new(
        documentId, "VDT-1", "CB-0000001", "J. Jansen", DateTimeOffset.UnixEpoch, "Utrecht", "3511 AW", "NLD", condition, null, Files);

    [Fact]
    public void Proof_HoldsWhatTheCarrierSent()
    {
        var proof = Proof(condition: ProofOfDeliveryCondition.Reservations) with { Remark = "Two pallets damaged" };

        (proof.DocumentId, proof.BookingReference, proof.CarrierBookingNumber, proof.ReceivedBy).Should().Be(("doc-1", "VDT-1", "CB-0000001", "J. Jansen"));
        (proof.Place, proof.Postcode, proof.Country, proof.Condition, proof.Remark).Should().Be(("Utrecht", "3511 AW", "NLD", ProofOfDeliveryCondition.Reservations, "Two pallets damaged"));
        proof.Files.Should().ContainSingle().Which.Should().Be(Cmr);
    }

    [Fact]
    public void Proofs_WithTheSameDocumentIdAndFiles_AreEqual_AndOthersAreNot()
    {
        Proof().Should().Be(Proof());
        Proof().GetHashCode().Should().Be(Proof().GetHashCode());
        Proof("doc-2").Should().NotBe(Proof());
        Proof().ToString().Should().Contain("doc-1");
    }

    [Fact]
    public void File_HoldsItsNameTypeAndContent()
    {
        (Cmr.Name, Cmr.ContentType, Cmr.Content).Should().Be(("cmr.pdf", "application/pdf", Cmr.Content));
        Cmr.Should().Be(Cmr with { });
        Cmr.ToString().Should().Contain("cmr.pdf");
    }
}
