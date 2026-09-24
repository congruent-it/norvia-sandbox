using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly Shipper BrouwerDranken = new(new ShipperId("SHP-0001"), "Brouwer Dranken", Active: true);
    private readonly TransportOrderRules _rules = new(maxTotalWeightKg: 24_000m);

    [Fact]
    public void Check_CompleteBooking_FindsNothingWrong()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking(), Now);

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_UnknownShipper_RejectsWithUnknownShipper()
    {
        var reasons = _rules.Check(null, CompleteBooking(), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.UnknownShipper);
    }

    [Fact]
    public void Check_InactiveShipper_RejectsWithInactiveShipper()
    {
        var reasons = _rules.Check(BrouwerDranken with { Active = false }, CompleteBooking(), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.InactiveShipper);
    }

    [Fact]
    public void Check_AnyFailure_HasAPlainLanguageMessage()
    {
        var reasons = _rules.Check(null, CompleteBooking(), Now);

        reasons.Single().Message.Should().Be("The Shipper is not known to Norvia.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Check_NoShipperReference_RejectsWithShipperReferenceMissing(string? shipperReference)
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { ShipperReference = shipperReference }, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.ShipperReferenceMissing);
    }

    [Fact]
    public void Check_ReferenceHeldForDifferentContent_RejectsWithShipperReferenceReused()
    {
        var holder = AcceptedWith(CompleteBooking());

        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { Instructions = "Call ahead" }, Now, holder);

        CodesOf(reasons).Should().Equal(RejectionCode.ShipperReferenceReused);
        reasons.Single().Message.Should().Be(
            "The Shipper reference 'BD-2026-0001' is already used for Transport Order TO-0000007, with different content.");
    }

    [Fact]
    public void Check_ReferenceHeldForTheSameContent_FindsNothingWrong()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking(), Now, AcceptedWith(CompleteBooking()));

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_ReferenceHeldAndOtherRulesBroken_ReportsEveryFailure()
    {
        var booking = CompleteBooking() with { GoodsLines = [] };

        var reasons = _rules.Check(BrouwerDranken, booking, Now, AcceptedWith(CompleteBooking()));

        CodesOf(reasons).Should().Equal(RejectionCode.ShipperReferenceReused, RejectionCode.GoodsLinesMissing);
    }

    [Fact]
    public void Check_ReferenceAtTheLengthLimit_FindsNothingWrong()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { ShipperReference = new string('R', 100) }, Now);

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_ReferenceOverTheLengthLimit_RejectsWithShipperReferenceTooLong()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { ShipperReference = new string('R', 101) }, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.ShipperReferenceTooLong);
        reasons.Single().Message.Should().Be("The Shipper reference is 101 characters long; it may be at most 100.");
    }

    [Fact]
    public void Check_NoCollectionAddress_RejectsWithCollectionAddressMissing()
    {
        var booking = CompleteBooking();
        booking = booking with { Collection = booking.Collection! with { Address = null } };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.CollectionAddressMissing);
    }

    [Fact]
    public void Check_NoDeliveryAddress_RejectsWithDeliveryAddressMissing()
    {
        var booking = CompleteBooking();
        booking = booking with { Delivery = booking.Delivery! with { Address = null } };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryAddressMissing);
    }

    [Fact]
    public void Check_CollectionAddressWithoutStreetAndCity_RejectsWithCollectionAddressIncompleteNamingTheGaps()
    {
        var booking = CompleteBooking();
        booking = booking with { Collection = booking.Collection! with { Address = new Address(" ", "3087 BM", null, "NL") } };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.CollectionAddressIncomplete);
        reasons.Single().Message.Should().Be("The collection address has no street, city.");
    }

    [Theory]
    [InlineData(null, "3511 AW", "Utrecht", "NL", "street")]
    [InlineData("Oudegracht 112", null, "Utrecht", "NL", "postcode")]
    [InlineData("Oudegracht 112", "3511 AW", null, "NL", "city")]
    [InlineData("Oudegracht 112", "3511 AW", "Utrecht", null, "country")]
    public void Check_DeliveryAddressWithAGap_RejectsWithDeliveryAddressIncomplete(string? street, string? postcode, string? city, string? country, string gap)
    {
        var booking = CompleteBooking();
        booking = booking with { Delivery = booking.Delivery! with { Address = new Address(street, postcode, city, country) } };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryAddressIncomplete);
        reasons.Single().Message.Should().Be($"The delivery address has no {gap}.");
    }

    [Fact]
    public void Check_NoDeliveryDetailsAtAll_RejectsWithDeliveryAddressAndConsigneeNameMissing()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { Delivery = null }, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryAddressMissing, RejectionCode.ConsigneeNameMissing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_NoConsigneeName_RejectsWithConsigneeNameMissing(bool consigneeGiven)
    {
        var booking = CompleteBooking();
        var consignee = consigneeGiven ? booking.Delivery!.Consignee! with { Name = "" } : null;
        booking = booking with { Delivery = booking.Delivery! with { Consignee = consignee } };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.ConsigneeNameMissing);
    }

    [Theory]
    [MemberData(nameof(IncompleteWindows))]
    public void Check_NoCompleteCollectionWindow_RejectsWithCollectionWindowMissing(TimeWindow? window)
    {
        var booking = CompleteBooking();
        booking = booking with
        {
            Collection = booking.Collection! with { Window = window },
            Delivery = booking.Delivery! with { Window = null },
        };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.CollectionWindowMissing);
    }

    [Fact]
    public void Check_CollectionWindowStartingInThePast_RejectsWithCollectionWindowInPast()
    {
        var booking = WithWindows(collection: new TimeWindow(Now.AddMinutes(-1), Now.AddHours(4)), delivery: null);

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.CollectionWindowInPast);
    }

    [Fact]
    public void Check_CollectionWindowStartingNow_FindsNothingWrong()
    {
        var booking = WithWindows(collection: new TimeWindow(Now, Now.AddHours(4)), delivery: null);

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_CollectionWindowEarliestAfterLatest_RejectsWithCollectionWindowReversed()
    {
        var booking = WithWindows(collection: new TimeWindow(Now.AddHours(5), Now.AddHours(4)), delivery: null);

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.CollectionWindowReversed);
    }

    [Theory]
    [MemberData(nameof(NoDeliveryWindow))]
    public void Check_NoDeliveryWindow_FindsNothingWrong(TimeWindow? window)
    {
        var booking = WithWindows(collection: new TimeWindow(Now.AddHours(1), Now.AddHours(4)), delivery: window);

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        reasons.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(HalfWindows))]
    public void Check_DeliveryWindowWithOnlyOneEnd_RejectsWithDeliveryWindowIncomplete(TimeWindow window)
    {
        var booking = WithWindows(collection: new TimeWindow(Now.AddHours(1), Now.AddHours(4)), delivery: window);

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryWindowIncomplete);
    }

    [Fact]
    public void Check_DeliveryWindowEarliestAfterLatest_RejectsWithDeliveryWindowReversed()
    {
        var booking = WithWindows(
            collection: new TimeWindow(Now.AddHours(1), Now.AddHours(4)),
            delivery: new TimeWindow(Now.AddHours(9), Now.AddHours(8)));

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryWindowReversed);
    }

    [Fact]
    public void Check_DeliveryWindowEndingBeforeCollectionStarts_RejectsWithDeliveryWindowBeforeCollection()
    {
        var booking = WithWindows(
            collection: new TimeWindow(Now.AddHours(5), Now.AddHours(8)),
            delivery: new TimeWindow(Now.AddHours(2), Now.AddHours(4)));

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DeliveryWindowBeforeCollection);
    }

    [Fact]
    public void Check_DeliveryWindowOverlappingTheCollectionWindow_FindsNothingWrong()
    {
        var booking = WithWindows(
            collection: new TimeWindow(Now.AddHours(5), Now.AddHours(8)),
            delivery: new TimeWindow(Now.AddHours(2), Now.AddHours(5)));

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        reasons.Should().BeEmpty();
    }

    public static TheoryData<TimeWindow?> IncompleteWindows() =>
    [
        (TimeWindow?)null,
        new TimeWindow(null, null),
        new TimeWindow(Now.AddHours(1), null),
        new TimeWindow(null, Now.AddHours(4)),
    ];

    public static TheoryData<TimeWindow?> NoDeliveryWindow() => [(TimeWindow?)null, new TimeWindow(null, null)];

    public static TheoryData<TimeWindow> HalfWindows() =>
    [
        new TimeWindow(Now.AddHours(6), null),
        new TimeWindow(null, Now.AddHours(10)),
    ];

    [Fact]
    public void Check_NoGoodsLines_RejectsWithGoodsLinesMissing()
    {
        var reasons = _rules.Check(BrouwerDranken, CompleteBooking() with { GoodsLines = [] }, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.GoodsLinesMissing);
    }

    [Theory]
    [InlineData("pallet")]
    [InlineData("parcel")]
    [InlineData("crate")]
    [InlineData("loose")]
    public void Check_PackageTypeFromTheList_FindsNothingWrong(string packageType)
    {
        var reasons = _rules.Check(BrouwerDranken, WithLine(new GoodsLine(packageType, 1, 10m, null)), Now);

        reasons.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("barrel")]
    [InlineData("Pallet")]
    public void Check_PackageTypeNotFromTheList_RejectsWithPackageTypeInvalid(string? packageType)
    {
        var reasons = _rules.Check(BrouwerDranken, WithLine(new GoodsLine(packageType, 1, 10m, null)), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.PackageTypeInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Check_QuantityBelowOne_RejectsWithQuantityInvalid(int? quantity)
    {
        var reasons = _rules.Check(BrouwerDranken, WithLine(new GoodsLine("parcel", quantity, 10m, null)), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.QuantityInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    public void Check_WeightNotAboveZero_RejectsWithWeightInvalid(double? weightKg)
    {
        var reasons = _rules.Check(BrouwerDranken, WithLine(new GoodsLine("parcel", 1, (decimal?)weightKg, null)), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.WeightInvalid);
    }

    [Theory]
    [InlineData(0, 80, 150)]
    [InlineData(120, -1, 150)]
    [InlineData(120, 80, 0)]
    public void Check_DimensionNotPositive_RejectsWithDimensionsInvalid(double length, double width, double height)
    {
        var line = new GoodsLine("pallet", 1, 10m, new Dimensions((decimal)length, (decimal)width, (decimal)height));

        var reasons = _rules.Check(BrouwerDranken, WithLine(line), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.DimensionsInvalid);
    }

    [Fact]
    public void Check_DimensionsPartlyGiven_FindsNothingWrong()
    {
        var reasons = _rules.Check(BrouwerDranken, WithLine(new GoodsLine("pallet", 1, 10m, new Dimensions(120m, null, null))), Now);

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_SecondGoodsLineBroken_SaysWhichLine()
    {
        var booking = CompleteBooking() with { GoodsLines = [new GoodsLine("pallet", 1, 10m, null), new GoodsLine("barrel", 1, 10m, null)] };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        reasons.Single().Message.Should().Be("Goods line 2 has package type 'barrel'; it must be one of pallet, parcel, crate, loose.");
    }

    [Fact]
    public void Check_BookingBreakingSeveralRules_ReportsEveryFailure()
    {
        var booking = new TransportOrderDetails(null, null, null, [new GoodsLine("barrel", 0, 0m, new Dimensions(-1m, 1m, 1m))], null);

        var reasons = _rules.Check(BrouwerDranken with { Active = false }, booking, Now);

        CodesOf(reasons).Should().Equal(
            RejectionCode.InactiveShipper,
            RejectionCode.ShipperReferenceMissing,
            RejectionCode.CollectionAddressMissing,
            RejectionCode.DeliveryAddressMissing,
            RejectionCode.ConsigneeNameMissing,
            RejectionCode.CollectionWindowMissing,
            RejectionCode.PackageTypeInvalid,
            RejectionCode.QuantityInvalid,
            RejectionCode.WeightInvalid,
            RejectionCode.DimensionsInvalid);
    }

    [Fact]
    public void Check_TotalWeightOverTheLimit_RejectsWithTotalWeightOverLimit()
    {
        var booking = CompleteBooking() with { GoodsLines = [new GoodsLine("pallet", 20, 12_000m, null), new GoodsLine("pallet", 20, 12_000.5m, null)] };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        CodesOf(reasons).Should().Equal(RejectionCode.TotalWeightOverLimit);
        reasons.Single().Message.Should().Be("The total weight of 24000.5 kg is over the limit of 24000 kg for a single Transport Order.");
    }

    [Fact]
    public void Check_TotalWeightExactlyAtTheLimit_FindsNothingWrong()
    {
        var booking = CompleteBooking() with { GoodsLines = [new GoodsLine("pallet", 20, 24_000m, null)] };

        var reasons = _rules.Check(BrouwerDranken, booking, Now);

        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Check_LowerLimitConfigured_AppliesThatLimit()
    {
        var rules = new TransportOrderRules(maxTotalWeightKg: 500m);

        var reasons = rules.Check(BrouwerDranken, CompleteBooking(), Now);

        CodesOf(reasons).Should().Equal(RejectionCode.TotalWeightOverLimit);
    }

    private static TransportOrder AcceptedWith(TransportOrderDetails details) => TransportOrder.Accept(
        BrouwerDranken.Id, details, TransportOrderNumber.FromSequence(7), new CorrelationId("corr-original"), Now.AddDays(-1));

    private static RejectionCode[] CodesOf(IEnumerable<RejectionReason> reasons) => [.. reasons.Select(reason => reason.Code)];

    private static TransportOrderDetails WithLine(GoodsLine line) => CompleteBooking() with { GoodsLines = [line] };

    private static TransportOrderDetails WithWindows(TimeWindow collection, TimeWindow? delivery)
    {
        var booking = CompleteBooking();
        return booking with
        {
            Collection = booking.Collection! with { Window = collection },
            Delivery = booking.Delivery! with { Window = delivery },
        };
    }

    private static TransportOrderDetails CompleteBooking() => new(
        ShipperReference: "BD-2026-0001",
        Collection: new CollectionDetails(
            new Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
            new TimeWindow(Now.AddDays(1), Now.AddDays(1).AddHours(4))),
        Delivery: new DeliveryDetails(
            new Address("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
            new Consignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", null),
            new TimeWindow(Now.AddDays(1).AddHours(6), Now.AddDays(1).AddHours(10))),
        GoodsLines:
        [
            new GoodsLine("pallet", 2, 850m, new Dimensions(120m, 80m, 150m)),
            new GoodsLine("crate", 4, 60.5m, null),
        ],
        Instructions: "Ring at the side entrance");
}
