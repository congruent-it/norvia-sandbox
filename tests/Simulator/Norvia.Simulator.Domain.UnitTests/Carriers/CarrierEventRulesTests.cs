using Norvia.Simulator.Domain.Carriers;

namespace Norvia.Simulator.Domain.UnitTests.Carriers;

public sealed class CarrierEventRulesTests
{
    [Theory]
    [InlineData(CarrierEvent.Collected, true)]
    [InlineData(CarrierEvent.Departed, true)]
    [InlineData(CarrierEvent.CollectionFailed, true)]
    [InlineData(CarrierEvent.Damaged, true)]
    [InlineData(CarrierEvent.Arrived, false)]
    [InlineData(CarrierEvent.OutForDelivery, false)]
    [InlineData(CarrierEvent.Delivered, false)]
    [InlineData(CarrierEvent.DeliveryFailed, false)]
    public void HappensAtPickup_OnlyWhatHappensBeforeTheGoodsAreOnTheirWay(CarrierEvent carrierEvent, bool atPickup)
    {
        carrierEvent.HappensAtPickup().Should().Be(atPickup);
    }

    [Fact]
    public void Remark_FailuresAndDamageSaySoAndProgressSaysNothing()
    {
        Enum.GetValues<CarrierEvent>().Where(e => e.Remark() is null)
            .Should().BeEquivalentTo([CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.Delivered]);
    }
}
