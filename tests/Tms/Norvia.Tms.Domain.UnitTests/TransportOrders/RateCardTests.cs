using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

/// <summary>
/// The rating rules of P2: which Rate Card version, Zone and Weight Break price a Transport Order, and the charge lines
/// that come of it. The sample Rate Card prices Rotterdam to Utrecht at 14.00 per 100 kg up to 500 kg and 12.40 up to
/// 2,000 kg, with a minimum charge of 45.00.
/// </summary>
public sealed class RateCardTests
{
    private static readonly Zone Rotterdam = new(new ZoneName("Rotterdam"), "NL", ["30", "31"]);
    private static readonly Zone Utrecht = new(new ZoneName("Utrecht"), "NL", ["35"]);
    private static readonly Zone UtrechtSciencePark = new(new ZoneName("Utrecht Science Park"), "NL", ["3584"]);

    [Theory]
    [InlineData(500, 70.00)] // on the boundary: the lighter band, 500 ÷ 100 × 14.00
    [InlineData(500.001, 62.00)] // just above it: the next band, 500.001 ÷ 100 × 12.40 = 62.000124
    [InlineData(2_000, 248.00)] // on the highest boundary: still priced, 2,000 ÷ 100 × 12.40
    public void FreightChargeFor_WeightOnOrAboveABoundary_UsesTheBandItsUpperLimitIncludes(decimal weightKg, decimal freight)
    {
        var charge = RateCardWith(SampleVersion()).FreightChargeFor(Details(weightKg: weightKg));

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(freight)));
    }

    [Fact]
    public void FreightChargeFor_WeightAboveTheHighestBreak_CantPrice()
    {
        var charge = RateCardWith(SampleVersion()).FreightChargeFor(Details(weightKg: 2_000.5m));

        charge.Should().BeNull();
    }

    [Fact]
    public void FreightChargeFor_FreightBelowTheMinimum_AddsAnAdjustmentUpToTheMinimum()
    {
        var charge = RateCardWith(SampleVersion()).FreightChargeFor(Details(weightKg: 100m));

        charge!.ChargeLines.Should().Equal(
            new ChargeLine(ChargeLineType.Freight, new Money(14.00m)),
            new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(31.00m)));
        charge.Total.Should().Be(new Money(45.00m));
    }

    [Fact]
    public void FreightChargeFor_FreightExactlyTheMinimum_HasNoAdjustment()
    {
        // 400 kg ÷ 100 × 14.00 = 56.00, not below the minimum.
        var version = SampleVersion(minimumCharge: 56.00m);

        var charge = RateCardWith(version).FreightChargeFor(Details(weightKg: 400m));

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(56.00m)));
    }

    [Fact]
    public void FreightChargeFor_FreightOffTheCent_RoundsEachLineAndAddsThemUp()
    {
        // 100.25 kg ÷ 100 × 14.00 = 14.035. Rounding only the total would keep the lines at 14.035 and 30.965; each line
        // is rounded to cents instead, half away from zero, and the adjustment makes up the difference to the minimum
        // from the rounded Freight line.
        var charge = RateCardWith(SampleVersion()).FreightChargeFor(Details(weightKg: 100.25m));

        charge!.ChargeLines.Should().Equal(
            new ChargeLine(ChargeLineType.Freight, new Money(14.04m)),
            new ChargeLine(ChargeLineType.MinimumChargeAdjustment, new Money(30.96m)));
        charge.Total.Should().Be(new Money(45.00m));
    }

    [Fact]
    public void FreightChargeFor_PostcodeInTwoZones_TheMostSpecificPrefixDecides()
    {
        // Utrecht (35) is listed before Utrecht Science Park (3584), so a first match would pick the wrong Zone.
        var version = SampleVersion() with
        {
            Zones = [Rotterdam, Utrecht, UtrechtSciencePark],
            Lanes =
            [
                Lane(Rotterdam, Utrecht, pricePer100Kg: 12.40m),
                Lane(Rotterdam, UtrechtSciencePark, pricePer100Kg: 20.00m),
            ],
        };

        var charge = RateCardWith(version).FreightChargeFor(Details(weightKg: 1_000m, deliveryPostcode: "3584 CS"));

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(200.00m)));
    }

    [Fact]
    public void FreightChargeFor_PostcodeOnlyInTheWiderZone_UsesTheWiderZone()
    {
        var version = SampleVersion() with
        {
            Zones = [Rotterdam, UtrechtSciencePark, Utrecht],
            Lanes =
            [
                Lane(Rotterdam, Utrecht, pricePer100Kg: 12.40m),
                Lane(Rotterdam, UtrechtSciencePark, pricePer100Kg: 20.00m),
            ],
        };

        var charge = RateCardWith(version).FreightChargeFor(Details(weightKg: 1_000m, deliveryPostcode: "3511 AW"));

        charge!.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, new Money(124.00m)));
    }

    [Fact]
    public void FreightChargeFor_CollectionJustAfterMidnightInAmsterdam_UsesTheVersionValidOnThatDay()
    {
        // 00:30 on 1 March in Amsterdam is still 29 February in UTC.
        var collection = new DateTimeOffset(2028, 3, 1, 0, 30, 0, TimeSpan.FromHours(1)).ToUniversalTime();
        var rateCard = RateCardWith(
            SampleVersion(version: 1, validFrom: new DateOnly(2028, 1, 1), validTo: new DateOnly(2028, 2, 29)),
            SampleVersion(version: 2, validFrom: new DateOnly(2028, 3, 1)));

        var charge = rateCard.FreightChargeFor(Details(weightKg: 1_000m, collectionEarliest: collection));

        charge!.RateCardVersion.Version.Should().Be(2);
    }

    [Fact]
    public void RateCardVersion_TwoZonesWithTheSamePrefix_Throws()
    {
        var act = () => SampleVersion() with { Zones = [Rotterdam, Utrecht, new Zone(new ZoneName("Rijnmond"), "NL", ["3000", "31"])] };

        act.Should().Throw<ArgumentException>().WithMessage("*Rotterdam*Rijnmond*31*");
    }

    [Fact]
    public void RateCardVersion_SamePrefixInTwoCountries_DoesNotOverlap()
    {
        var act = () => SampleVersion() with { Zones = [Rotterdam, Utrecht, new Zone(new ZoneName("Antwerpen"), "BE", ["20", "35"])] };

        act.Should().NotThrow();
    }

    [Fact]
    public void RateCardVersion_PrefixesDifferingOnlyInSpacesOrCase_Overlap()
    {
        var act = () => SampleVersion() with
        {
            Zones = [new Zone(new ZoneName("Waalhaven"), "NL", ["3087 bm"]), new Zone(new ZoneName("Port"), "NL", ["3087BM"])],
        };

        act.Should().Throw<ArgumentException>();
    }

    private static RateCardVersion SampleVersion(int version = 1, DateOnly? validFrom = null, DateOnly? validTo = null, decimal minimumCharge = 45.00m) => new(
        version,
        validFrom ?? new DateOnly(2020, 1, 1),
        validTo,
        [Rotterdam, Utrecht],
        [
            new Lane(
                Rotterdam.Name,
                Utrecht.Name,
                [new WeightBreak(500m, new Money(14.00m)), new WeightBreak(2_000m, new Money(12.40m))],
                new Money(minimumCharge)),
        ]);

    private static Lane Lane(Zone origin, Zone destination, decimal pricePer100Kg) =>
        new(origin.Name, destination.Name, [new WeightBreak(2_000m, new Money(pricePer100Kg))], new Money(45.00m));

    private static RateCard RateCardWith(params RateCardVersion[] versions) =>
        new(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), versions);

    private static TransportOrderDetails Details(decimal weightKg, string deliveryPostcode = "3511 AW", DateTimeOffset? collectionEarliest = null)
    {
        var earliest = collectionEarliest ?? new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        return TransportOrderTests.SampleDetails() with
        {
            Collection = new CollectionDetails(
                new Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
                new TimeWindow(earliest, earliest.AddHours(4))),
            Delivery = new DeliveryDetails(new Address("Somewhere 1", deliveryPostcode, "Utrecht", "NL"), null, null),
            GoodsLines = [new GoodsLine("pallet", 1, weightKg, null)],
        };
    }
}
