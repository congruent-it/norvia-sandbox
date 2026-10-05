using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Web.Shipments;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ProofOfDeliveryTextTests
{
    [Theory]
    [InlineData(DeliveryCondition.Clean, "Clean")]
    [InlineData(DeliveryCondition.Reservations, "With reservations")]
    public void Condition_SaysWhetherTheGoodsWereTakenCleanOrWithReservations(DeliveryCondition condition, string expected) =>
        ProofOfDeliveryText.Condition(condition).Should().Be(expected);

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(48 * 1024, "48 KB")]
    [InlineData(2_411_724, "2.3 MB")]
    public void Size_ShowsTheLargestFittingUnit(long bytes, string expected) =>
        ProofOfDeliveryText.Size(bytes).Should().Be(expected);
}
