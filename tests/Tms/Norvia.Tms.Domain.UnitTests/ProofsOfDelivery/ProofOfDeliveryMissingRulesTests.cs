using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Domain.UnitTests.ProofsOfDelivery;

/// <summary>P6's missing rule: a Delivered Shipment's Proof of delivery flag for the Planner's list.</summary>
public sealed class ProofOfDeliveryMissingRulesTests
{
    private static readonly DateTimeOffset DeliveredAt = new(2026, 9, 24, 8, 0, 0, TimeSpan.FromHours(2));
    private readonly ProofOfDeliveryMissingRules _rules = new(TimeSpan.FromHours(48));

    [Fact]
    public void IsMissing_JustBeforeTheThreshold_IsFalse()
    {
        _rules.IsMissing(DeliveredAt, DeliveredAt.AddHours(48).AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsMissing_AtTheThreshold_IsTrue()
    {
        _rules.IsMissing(DeliveredAt, DeliveredAt.AddHours(48)).Should().BeTrue();
    }

    [Fact]
    public void IsMissing_MeasuresFromTheInstant_NotTheOffsetItKeeps()
    {
        _rules.IsMissing(DeliveredAt, DeliveredAt.ToUniversalTime().AddHours(48)).Should().BeTrue();
    }

    [Fact]
    public void IsMissing_WithTheThresholdUnset_IsNeverTrue()
    {
        ProofOfDeliveryMissingRules.Unset.IsMissing(DeliveredAt, DeliveredAt.AddYears(1)).Should().BeFalse();
    }
}
