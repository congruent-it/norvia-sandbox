using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class ProofOfDeliveryTextTests
{
    [Theory]
    [InlineData(ProofOfDeliveryCondition.Clean, "Clean (CLEAN)")]
    [InlineData(ProofOfDeliveryCondition.Reservations, "With reservations (RESERVATIONS)")]
    public void Condition_ReadsWithTheCarriersCode(ProofOfDeliveryCondition condition, string words) =>
        ProofOfDeliveryText.Condition(condition).Should().Be(words);

    [Fact]
    public void Condition_UnknownHasNoWords()
    {
        var words = () => ProofOfDeliveryText.Condition((ProofOfDeliveryCondition)99);

        words.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SampleCmr_IsABundledPdf()
    {
        var cmr = SampleCmr.Read();

        (cmr.Name, cmr.ContentType).Should().Be(("sample-cmr.pdf", "application/pdf"));
        cmr.Content.Take(5).Should().Equal("%PDF-"u8.ToArray());
        cmr.Content.Length.Should().BeLessThan(10 * 1024 * 1024);
    }
}
