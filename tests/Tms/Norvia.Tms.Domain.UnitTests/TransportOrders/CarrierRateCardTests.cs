using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

/// <summary>
/// A Carrier Rate Card has the shape of a Rate Card and is priced by the same calculation (P9), so it fails with the
/// same codes. The sample prices Rotterdam to Utrecht at 11.00 per 100 kg up to 500 kg and 9.50 up to 2,000 kg, with a
/// minimum charge of 35.00.
/// </summary>
public sealed class CarrierRateCardTests
{
    private static readonly Zone Rotterdam = new(new ZoneName("Rotterdam"), "NL", ["30", "31"]);
    private static readonly Zone Utrecht = new(new ZoneName("Utrecht"), "NL", ["35"]);

    [Fact]
    public void Rate_OrderItCovers_PricesFromTheLaneAndRecordsTheVersionUsed()
    {
        var charge = CarrierRateCardWith(SampleVersion(version: 3)).Rate(Details(weightKg: 1_000m)).FreightCharge;

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(95.00m)));
        charge.RateCardVersion.Should().Be(new RateCardVersionReference(new RateCardId("CRC-0001"), 3));
    }

    [Fact]
    public void Rate_CollectionDate_ChoosesTheVersionValidThatDay()
    {
        var carrierRateCard = CarrierRateCardWith(
            SampleVersion(version: 1, validFrom: new DateOnly(2026, 1, 1), validTo: new DateOnly(2026, 9, 24)),
            SampleVersion(version: 2, validFrom: new DateOnly(2026, 9, 25)));

        var charge = carrierRateCard.Rate(Details(weightKg: 1_000m)).FreightCharge;

        charge!.RateCardVersion.Version.Should().Be(2);
    }

    [Fact]
    public void Rate_NoVersionValidOnTheCollectionDate_FailsWithNoValidRateCardVersion()
    {
        var outcome = CarrierRateCardWith(SampleVersion(validFrom: new DateOnly(2026, 9, 26))).Rate(Details(weightKg: 1_000m));

        outcome.Failure.Should().Be(new RatingFailure(
            RatingFailureCode.NoValidRateCardVersion,
            "No version of Carrier Rate Card CRC-0001 is valid on the collection date, 2026-09-25."));
    }

    [Fact]
    public void Rate_NoLaneBetweenTheZones_FailsWithNoMatchingLane()
    {
        var details = Details(weightKg: 1_000m) with
        {
            Delivery = new DeliveryDetails(new Address("Königsallee 1", "40212", "Düsseldorf", "DE"), null, null),
        };

        var outcome = CarrierRateCardWith(SampleVersion()).Rate(details);

        outcome.Failure.Should().Be(new RatingFailure(
            RatingFailureCode.NoMatchingLane,
            "Carrier Rate Card CRC-0001 version 1 has no Lane from Rotterdam to DE 40212, which lies in no Zone."));
    }

    [Theory]
    [InlineData(500, 55.00)] // on the boundary: the lighter band
    [InlineData(500.001, 47.50)] // just above it: the next band, 500.001 ÷ 100 × 9.50 = 47.500095
    public void Rate_WeightOnOrAboveABoundary_UsesTheBandItsUpperLimitIncludes(decimal weightKg, decimal freight)
    {
        var charge = CarrierRateCardWith(SampleVersion()).Rate(Details(weightKg: weightKg)).FreightCharge;

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(freight)));
    }

    [Fact]
    public void Rate_WeightAboveTheHighestBreak_FailsWithWeightAboveHighestBreak()
    {
        var outcome = CarrierRateCardWith(SampleVersion()).Rate(Details(weightKg: 2_000.5m));

        outcome.Failure.Should().Be(new RatingFailure(
            RatingFailureCode.WeightAboveHighestBreak,
            "The total weight of 2000.5 kg is above the highest Weight Break, up to 2000 kg, of the Lane from Rotterdam to Utrecht in Carrier Rate Card CRC-0001 version 1."));
    }

    [Fact]
    public void Rate_FreightBelowTheMinimum_AddsAnAdjustmentUpToTheMinimum()
    {
        var charge = CarrierRateCardWith(SampleVersion()).Rate(Details(weightKg: 100m)).FreightCharge;

        charge!.ChargeLines.Should().Equal(
            new ChargeLine(ChargeLineType.Freight, new Money(11.00m)),
            new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(24.00m)));
    }

    [Fact]
    public void Rate_FreightOffTheCent_RoundsHalfAwayFromZero()
    {
        // 150.5 kg ÷ 100 × 11.00 = 16.555 rounds up to 16.56.
        var charge = CarrierRateCardWith(SampleVersion(minimumCharge: 10.00m)).Rate(Details(weightKg: 150.5m)).FreightCharge;

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(16.56m)));
    }

    private static RateCardVersion SampleVersion(int version = 1, DateOnly? validFrom = null, DateOnly? validTo = null, decimal minimumCharge = 35.00m) => new(
        version,
        validFrom ?? new DateOnly(2020, 1, 1),
        validTo,
        [Rotterdam, Utrecht],
        [
            new Lane(
                Rotterdam.Name,
                Utrecht.Name,
                [new WeightBreak(500m, new Money(11.00m)), new WeightBreak(2_000m, new Money(9.50m))],
                new Money(minimumCharge)),
        ]);

    private static CarrierRateCard CarrierRateCardWith(params RateCardVersion[] versions) =>
        new(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), versions);

    private static TransportOrderDetails Details(decimal weightKg)
    {
        var earliest = new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        return TransportOrderTests.SampleDetails() with
        {
            Collection = new CollectionDetails(
                new Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
                new TimeWindow(earliest, earliest.AddHours(4))),
            Delivery = new DeliveryDetails(new Address("Somewhere 1", "3511 AW", "Utrecht", "NL"), null, null),
            GoodsLines = [new GoodsLine("pallet", 1, weightKg, null)],
        };
    }
}
