using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// The rating rules (P2) at the TMS's boundary: which Rate Card version, Zone and Weight Break price an order, and the
/// charge lines that come of it. The test seed's Rate Card prices Rotterdam to Utrecht, until 2040, at 14.00 per 100 kg
/// up to 500 kg and 12.40 up to 2,000 kg, with a minimum charge of 45.00; Utrecht Science Park (3584) inside Utrecht
/// at 15.00; and from 2040 Rotterdam to Utrecht at 13.00 per 100 kg up to 2,000 kg.
/// </summary>
public sealed class TransportOrderRatingRulesTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_FreightBelowTheMinimum_AddsAnAdjustmentLineUpToTheMinimum()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).Weighing(100m), correlationId, _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        ChargeLines(order["freightCharge"]!).Should().Equal(("Freight", 14.00m), ("MinimumChargeAdjustment", 31.00m));
        order["freightCharge"]!["total"]!.GetValue<decimal>().Should().Be(45.00m, "the total equals the Lane's minimum charge");

        var published = JsonNode.Parse((await tms.PublishedAsync(correlationId, "TransportOrderRated", _cancellationToken)).Single().Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateTransportOrderRated(published);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        ChargeLines(published["freightCharge"]!).Should().Equal(("Freight", 14.00m), ("MinimumChargeAdjustment", 31.00m));
    }

    [Theory]
    [InlineData(500, 70.00)] // 500 ÷ 100 × 14.00, not × 12.40
    [InlineData(2_000, 248.00)] // 2,000 ÷ 100 × 12.40, not × 9.75
    public async Task Submit_WeightExactlyOnAWeightBreakBoundary_UsesTheBandItIsTheUpperLimitOf(decimal weightKg, decimal freight)
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).Weighing(weightKg), TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        ChargeLines(order["freightCharge"]!).Should().Equal(("Freight", freight));
    }

    [Fact]
    public async Task Submit_DeliveryPostcodeInANarrowerZone_TheMostSpecificPrefixDecidesTheZone()
    {
        var booking = Bookings.Valid(TmsCalls.NewReference()).Weighing(1_000m).DeliveredTo("3584 CS");
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        ChargeLines(order["freightCharge"]!).Should().Equal([("Freight", 150.00m)], "Utrecht Science Park's 15.00, not Utrecht's 12.40");
    }

    [Fact]
    public async Task Submit_CollectionJustAfterMidnightInAmsterdam_UsesTheVersionValidOnThatDayThere()
    {
        // 00:30 on 1 January 2040 in Amsterdam (CET) is 23:30 on 31 December 2039 in UTC, the last day of version 1.
        var collection = new DateTimeOffset(2040, 1, 1, 0, 30, 0, TimeSpan.FromHours(1));
        var booking = Bookings.Valid(TmsCalls.NewReference()).CollectedFrom(collection.ToUniversalTime());
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        order["freightCharge"]!["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(2);
        ChargeLines(order["freightCharge"]!).Should().Equal([("Freight", 118.37m)], "910.5 kg ÷ 100 × 13.00 = 118.365, version 2's price");
    }

    [Fact]
    public async Task Submit_FreightOffTheCent_RoundsEachChargeLineAndTotalsTheRoundedLines()
    {
        // 100.25 kg ÷ 100 × 14.00 = 14.035. Rounding only the total would leave the lines at 14.035 and 30.965; each
        // line is rounded to cents instead, half away from zero, and the adjustment makes the rounded Freight up to 45.00.
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).Weighing(100.25m), TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        ChargeLines(order["freightCharge"]!).Should().Equal(("Freight", 14.04m), ("MinimumChargeAdjustment", 30.96m));
        order["freightCharge"]!["total"]!.GetValue<decimal>().Should().Be(45.00m);
    }

    private static List<(string Type, decimal Amount)> ChargeLines(JsonNode freightCharge) =>
        [.. freightCharge["chargeLines"]!.AsArray().Select(line => (line!["type"]!.GetValue<string>(), line["amount"]!.GetValue<decimal>()))];
}
